using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace RShared.Redis.Tests;

/// <summary>
/// Key events: подписка на каналы __keyevent@*:kind, диспетчеризация по kind,
/// hosted-сервис только при наличии подписок, дверь IRedisNative
/// </summary>
public sealed class KeyEventsTests
{
	private readonly IConnectionMultiplexer _multiplexer;
	private readonly IDatabase _database;
	private readonly ISubscriber _subscriber;

	public KeyEventsTests()
	{
		(_, _multiplexer, _database) = RedisMocks.Database();
		_subscriber = Substitute.For<ISubscriber>();
		_multiplexer.GetSubscriber(Arg.Any<object?>()).Returns(_subscriber);
	}

	private RedisKeyEventService BuildService(RedisKeyEventRegistry registry)
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);
		return new RedisKeyEventService(connection, registry, Substitute.For<ILogger<RedisKeyEventService>>());
	}

	[Fact]
	public async Task Subscribes_to_the_channel_of_each_event_kind()
	{
		var registry = new RedisKeyEventRegistry();
		registry.Add(RedisKeyEventKind.Expired, _ => Task.CompletedTask);
		registry.Add(RedisKeyEventKind.Deleted, _ => Task.CompletedTask);
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);

		await service.StartAsync(default);

		await _subscriber.Received(1).SubscribeAsync((RedisChannel)"__keyevent@*:expired", Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
		await _subscriber.Received(1).SubscribeAsync((RedisChannel)"__keyevent@*:del", Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task The_key_arrives_at_handlers_of_its_kind_only()
	{
		var expired = new List<string>();
		var registry = new RedisKeyEventRegistry();
		registry.Add(RedisKeyEventKind.Expired, e => { expired.Add(e.Key); return Task.CompletedTask; });
		registry.Add(RedisKeyEventKind.Deleted, _ => throw new InvalidOperationException("wrong kind"));

		var captured = new List<Action<RedisChannel, RedisValue>>();
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(captured.Add), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);
		await service.StartAsync(default);

		// payload expired приходит в каждую подписку: своя dispatch-ит, чужая молчит (kind в замыкании)
		foreach (var action in captured)
		{
			action((RedisChannel)"__keyevent@*:expired", (RedisValue)"cache:hits");
		}

		await Task.Delay(50);

		Assert.Equal(["cache:hits"], expired);
	}

	[Fact]
	public async Task A_failing_handler_does_not_break_the_next_one()
	{
		var secondCalled = false;
		var registry = new RedisKeyEventRegistry();
		registry.Add(RedisKeyEventKind.Expired, _ => throw new InvalidOperationException("boom"));
		registry.Add(RedisKeyEventKind.Expired, _ => { secondCalled = true; return Task.CompletedTask; });

		Action<RedisChannel, RedisValue>? captured = null;
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(a => captured = a), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);
		await service.StartAsync(default);

		captured!.Invoke((RedisChannel)"__keyevent@*:expired", (RedisValue)"k");
		await Task.Delay(50);

		Assert.True(secondCalled);
	}

	[Fact]
	public async Task Stop_unsubscribes()
	{
		var registry = new RedisKeyEventRegistry();
		registry.Add(RedisKeyEventKind.Expired, _ => Task.CompletedTask);
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);
		await service.StartAsync(default);

		await service.StopAsync(default);

		// отписка прошла (перегрузко-независимо: impl зовёт 1-арную UnsubscribeAsync)
		var unsubs = _subscriber.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "UnsubscribeAsync").ToList();
		Assert.Single(unsubs);
	}

	[Fact]
	public void AddRedisKeyEvents_registers_the_hosted_service_once()
	{
		var services = new ServiceCollection();
		services.AddRedis(o => o.ConnectionString = "localhost:6379");
		services.AddRedisKeyEvents(_ => { });
		services.AddRedisKeyEvents(_ => { });

		var hosted = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
		Assert.Single(hosted, hosted.First(d => d.ImplementationType == typeof(RedisKeyEventService)));
	}

	[Fact]
	public void Without_AddRedisKeyEvents_the_service_is_not_registered()
	{
		var services = new ServiceCollection();
		services.AddRedis(o => o.ConnectionString = "localhost:6379");

		Assert.DoesNotContain(services, d => d.ImplementationType == typeof(RedisKeyEventService));
	}

	[Fact]
	public async Task Native_opens_the_raw_database()
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);

		Assert.Same(_database, new RedisNative(connection).Database);
		await Task.CompletedTask;
	}

	[Fact]
	public void Suffixes_map_kinds_to_channels()
	{
		var registry = new RedisKeyEventRegistry();
		registry.Add(RedisKeyEventKind.Expired, _ => Task.CompletedTask);
		registry.Add(RedisKeyEventKind.Deleted, _ => Task.CompletedTask);
		registry.Add(RedisKeyEventKind.Evicted, _ => Task.CompletedTask);
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);

		service.StartAsync(default).GetAwaiter().GetResult();

		_subscriber.Received(1).SubscribeAsync((RedisChannel)"__keyevent@*:expired", Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
		_subscriber.Received(1).SubscribeAsync((RedisChannel)"__keyevent@*:del", Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
		_subscriber.Received(1).SubscribeAsync((RedisChannel)"__keyevent@*:evicted", Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Handler_exception_is_contained()
	{
		var called = false;
		var registry = new RedisKeyEventRegistry();
		registry.Add(RedisKeyEventKind.Expired, _ => throw new InvalidOperationException("boom"));
		registry.Add(RedisKeyEventKind.Expired, _ => { called = true; return Task.CompletedTask; });

		Action<RedisChannel, RedisValue>? captured = null;
		_subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(a => captured = a), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var service = BuildService(registry);
		service.StartAsync(default).GetAwaiter().GetResult();

		// прямой вызов диспетчеризации: событие доходит до обоих, падение первого не роняет второе
		captured!.Invoke((RedisChannel)"__keyevent@*:expired", (RedisValue)"k");
		await Task.Delay(50);

		Assert.True(called);
	}

	[Fact]
	public async Task Registry_lookup_survives_an_empty_collection()
	{
		var services = new ServiceCollection();

		// без AddRedis реестров нет: First() бросил бы, FirstOrCreate-семантика — нет
		services.AddRedisKeyEvents(_ => { });
		var delivered = 0;
		// именно Action-перегрузка: её обёртка должна вызывать действие
		services.AddRedisSubscription<CacheTests.Item>(item => delivered += item.Number);

		// sync-обёртка подписки вызывается и доходит до действия
		var subscription = services.BuildServiceProvider().GetRequiredService<RedisSubscriptionRegistry>();
		await subscription.Snapshot().Single().Handler(new CacheTests.Item(1, "x"));
		Assert.Equal(1, delivered);

		// оба реестра зарегистрированы TryAddSingleton-инстансом и резолвятся
		Assert.NotNull(services.BuildServiceProvider().GetRequiredService<RedisKeyEventRegistry>());
		Assert.NotNull(services.BuildServiceProvider().GetRequiredService<RedisSubscriptionRegistry>());
	}

	[Fact]
	public async Task Sync_wrapper_invokes_the_action()
	{
		var services = new ServiceCollection();
		var hit = false;
		services.AddRedisKeyEvents(e => hit = e.Key == "k");

		var registry = services.BuildServiceProvider().GetRequiredService<RedisKeyEventRegistry>();
		await registry.Snapshot().Single().Handler(new RedisKeyEvent("k", RedisKeyEventKind.Expired));
		Assert.True(hit);
	}

	[Fact]
	public void AddRedisKeyEvents_rejects_null_handler()
	{
		Assert.Throws<ArgumentNullException>(() => new ServiceCollection()
			.AddRedisKeyEvents((Func<RedisKeyEvent, Task>)null!));
	}

	[Fact]
	public void Subscription_without_AddRedis_creates_its_own_registry()
	{
		var services = new ServiceCollection();
		services.AddRedisKeyEvents(_ => { });

		// без AddRedis реестра нет — lookup возвращает null, а не бросает First()
		Assert.Single(services.Where(d => d.ServiceType == typeof(RedisKeyEventRegistry)));
	}

	[Fact]
	public void AddRedisKeyEvents_applies_configure()
	{
		var services = new ServiceCollection();
		services.AddRedis(o => o.ConnectionString = "localhost:6379");
		services.AddRedisKeyEvents(_ => { }, o => o.Events = [RedisKeyEventKind.Expired, RedisKeyEventKind.Deleted]);

		var registry = services.BuildServiceProvider().GetRequiredService<RedisKeyEventRegistry>();
		Assert.Equal(2, registry.Snapshot().Count);
	}

	[Fact]
	public void AddRedisKeyEvents_enriches_an_existing_registry()
	{
		var services = new ServiceCollection();
		services.AddRedis(o => o.ConnectionString = "localhost:6379");
		services.AddRedisKeyEvents(_ => { });
		var first = services.First(d => d.ServiceType == typeof(RedisKeyEventRegistry)).ImplementationInstance;

		services.AddRedisKeyEvents(_ => { });

		// реестр один и тот же — вторая подписка добавилась в него, а не создала новый
		Assert.Same(first, services.First(d => d.ServiceType == typeof(RedisKeyEventRegistry)).ImplementationInstance);
		Assert.Equal(2, ((RedisKeyEventRegistry)first!).Snapshot().Count);

		// реестр резолвится из провайдера: регистрация хоста не потерялась
		Assert.Same(first, services.BuildServiceProvider().GetRequiredService<RedisKeyEventRegistry>());
	}

	[Fact]
	public async Task Start_without_handlers_stays_silent()
	{
		var service = BuildService(new RedisKeyEventRegistry());

		await service.StartAsync(default);
		await service.StopAsync(default);

		await _subscriber.DidNotReceiveWithAnyArgs().SubscribeAsync(default, default(Action<RedisChannel, RedisValue>)!, default);
		await _subscriber.DidNotReceiveWithAnyArgs().UnsubscribeAsync(default, default(Action<RedisChannel, RedisValue>)!, default);
	}

}
