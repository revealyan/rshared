using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace RShared.Redis.Tests;

/// <summary>
/// Pub/sub: публикация в канал типа с префиксом, подписки реестром,
/// диспетчеризация (порядок, poison, упавший хендлер), отписка на стопе
/// </summary>
public sealed class PubSubTests
{
	private readonly IConnectionMultiplexer _multiplexer;
	private readonly ISubscriber _subscriber;

	public PubSubTests()
	{
		(_, _multiplexer, _) = RedisMocks.Database();
		_subscriber = Substitute.For<ISubscriber>();
		_multiplexer.GetSubscriber(Arg.Any<object?>()).Returns(_subscriber);
	}

	private RedisPublisher BuildPublisher(string channelPrefix = "")
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);
		return new RedisPublisher(connection, new RedisOption { ConnectionString = "x", ChannelPrefix = channelPrefix });
	}

	private RedisSubscriberService BuildService(RedisSubscriptionRegistry registry, string channelPrefix = "")
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);
		return new RedisSubscriberService(connection, registry,
			new RedisOption { ConnectionString = "x", ChannelPrefix = channelPrefix },
			Substitute.For<ILogger<RedisSubscriberService>>());
	}

	[Fact]
	public async Task Publish_goes_to_the_channel_of_the_type_with_prefix()
	{
		var publisher = BuildPublisher("app-");

		await publisher.PublishAsync(new OrderPaid(42));

		await _subscriber.Received(1).PublishAsync(
			(RedisChannel)"app-OrderPaid",
			Arg.Is<RedisValue>(v => v.ToString().Contains("\"Amount\":42")),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Subscriber_subscribes_to_each_message_type_once()
	{
		var registry = new RedisSubscriptionRegistry();
		registry.Add<OrderPaid>(_ => Task.CompletedTask);
		registry.Add<OrderPaid>(_ => Task.CompletedTask);
		registry.Add<OrderShipped>(_ => Task.CompletedTask);
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry, "app-");

		await service.StartAsync(default);

		await _subscriber.Received(1).SubscribeAsync((RedisChannel)"app-OrderPaid", Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
		await _subscriber.Received(1).SubscribeAsync((RedisChannel)"app-OrderShipped", Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Subscriber_without_subscriptions_stays_silent()
	{
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(new RedisSubscriptionRegistry());

		await service.StartAsync(default);
		await service.StopAsync(default);

		await _subscriber.DidNotReceiveWithAnyArgs().SubscribeAsync(default, default(Action<RedisChannel, RedisValue>)!, default);
		await _subscriber.DidNotReceiveWithAnyArgs().UnsubscribeAsync(default(RedisChannel), default(Action<RedisChannel, RedisValue>)!, default);
	}

	[Fact]
	public async Task Delivered_messages_reach_all_handlers_in_order()
	{
		var delivered = new List<int>();
		var registry = new RedisSubscriptionRegistry();
		registry.Add<OrderPaid>(_ => { delivered.Add(1); return Task.CompletedTask; });
		registry.Add<OrderPaid>(_ => { delivered.Add(2); return Task.CompletedTask; });

		Action<RedisChannel, RedisValue>? captured = null;
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(a => captured = a), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);
		await service.StartAsync(default);

		captured!.Invoke((RedisChannel)"OrderPaid", (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new OrderPaid(42)));

		Assert.Equal([1, 2], delivered);
	}

	[Fact]
	public async Task Poison_body_is_dropped_without_killing_the_subscription()
	{
		var calls = 0;
		var registry = new RedisSubscriptionRegistry();
		registry.Add<OrderPaid>(_ => { calls++; return Task.CompletedTask; });

		Action<RedisChannel, RedisValue>? captured = null;
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(a => captured = a), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);
		await service.StartAsync(default);

		captured!.Invoke((RedisChannel)"OrderPaid", (RedisValue)"not-json"u8.ToArray());

		Assert.Equal(0, calls);
	}

	[Fact]
	public async Task Failing_handler_does_not_break_the_next_one()
	{
		var secondCalled = false;
		var registry = new RedisSubscriptionRegistry();
		registry.Add<OrderPaid>(_ => throw new InvalidOperationException("boom"));
		registry.Add<OrderPaid>(_ => { secondCalled = true; return Task.CompletedTask; });

		Action<RedisChannel, RedisValue>? captured = null;
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(a => captured = a), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);
		await service.StartAsync(default);

		captured!.Invoke((RedisChannel)"OrderPaid", (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new OrderPaid(42)));
		await Task.Delay(50);

		Assert.True(secondCalled);
	}

	[Fact]
	public async Task Stop_unsubscribes_every_channel()
	{
		var registry = new RedisSubscriptionRegistry();
		registry.Add<OrderPaid>(_ => Task.CompletedTask);
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);
		await service.StartAsync(default);

		await service.StopAsync(default);
		await service.StopAsync(default);

		// повторный стоп чист: отписка один раз
		await _subscriber.Received(1).UnsubscribeAsync((RedisChannel)"OrderPaid", Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public void AddRedisSubscription_rejects_null_handler()
	{
		Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddRedisSubscription<OrderPaid>(null!));
	}

	[Fact]
	public void AddRedisSubscription_collects_handlers_in_the_registry()
	{
		var services = new ServiceCollection();
		services.AddRedis(o => o.ConnectionString = "localhost:6379");
		services.AddRedisSubscription<OrderPaid>(_ => Task.CompletedTask);

		var registry = services.BuildServiceProvider().GetRequiredService<RedisSubscriptionRegistry>();
		Assert.Single(registry.Snapshot());
	}

	private sealed record OrderPaid(int Amount);

	private sealed record OrderShipped(int Boxes);
}
