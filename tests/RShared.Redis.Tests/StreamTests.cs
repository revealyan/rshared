using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace RShared.Redis.Tests;

/// <summary>
/// Стримы: публикация XADD с триммингом из опции хендлера, consumer-группа с начала стрима,
/// доставка с ретраями и DLQ, poison, отписка/стоп циклов, дефолты опции
/// </summary>
public sealed class StreamTests
{
	private readonly IConnectionMultiplexer _multiplexer;
	private readonly IDatabase _database;

	public StreamTests()
	{
		(_, _multiplexer, _database) = RedisMocks.Database();
	}

	private RedisStreamPublisher BuildPublisher(RedisStreamHandlerRegistry? handlers = null, string keyPrefix = "")
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);
		return new RedisStreamPublisher(connection, new RedisOption { ConnectionString = "x", KeyPrefix = keyPrefix }, handlers ?? new RedisStreamHandlerRegistry());
	}

	private RedisStreamConsumerService BuildConsumer(RedisStreamHandlerRegistry registry, string keyPrefix = "")
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);
		return new RedisStreamConsumerService(connection, registry,
			new RedisOption { ConnectionString = "x", KeyPrefix = keyPrefix },
			Substitute.For<ILogger<RedisStreamConsumerService>>());
	}

	private static StreamEntry Entry(int amount, string body = """{"Amount":7}""") =>
		new((RedisValue)"1-1", [new NameValueEntry("body", (RedisValue)JsonSerializer.SerializeToUtf8Bytes(new OrderPaid(amount))), new NameValueEntry("type", "OrderPaid")]);

	private static NameValueEntry[] Pairs(object payload) =>
		[new NameValueEntry("body", (RedisValue)JsonSerializer.SerializeToUtf8Bytes(payload)), new NameValueEntry("type", "OrderPaid")];

	[Fact]
	public async Task Publish_appends_to_the_stream_of_the_type()
	{
		var publisher = BuildPublisher(keyPrefix: "app:");

		await publisher.PublishAsync(new OrderPaid(42));

		var add = SingleStreamAdd();
		Assert.Equal((RedisKey)"app:stream:OrderPaid", (RedisKey)add[0]!);
		Assert.Equal(true, add[4]); // приблизительный тримминг MAXLEN ~
		var pairs = Assert.IsType<NameValueEntry[]>(add[1]);
		Assert.Equal("body", pairs[0].Name.ToString());
		Assert.Equal("OrderPaid", pairs[1].Value.ToString());
		Assert.Equal(100_000L, Convert.ToInt64(add[3]));
	}

	[Fact]
	public async Task Publish_trims_by_the_handler_option_and_zero_disables()
	{
		var handlers = new RedisStreamHandlerRegistry();
		handlers.Add<OrderPaid>(_ => Task.CompletedTask, new RedisStreamOption { MaxStreamLength = 500 });
		var publisher = BuildPublisher(handlers);

		await publisher.PublishAsync(new OrderPaid(1));

		Assert.Equal(500L, Convert.ToInt64(SingleStreamAdd()[3]));

		handlers = new RedisStreamHandlerRegistry();
		handlers.Add<OrderPaid>(_ => Task.CompletedTask, new RedisStreamOption { MaxStreamLength = 0 });
		_database.ClearReceivedCalls();
		publisher = BuildPublisher(handlers);

		await publisher.PublishAsync(new OrderPaid(1));

		// 0 — не триммим: параметр maxLength приходит null
		Assert.Null(SingleStreamAdd()[3]);
	}

	private object?[] SingleStreamAdd()
	{
		var adds = _database.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "StreamAddAsync").ToList();
		return Assert.Single(adds).GetArguments();
	}

	[Fact]
	public async Task Consumer_creates_the_group_from_the_beginning()
	{
		var registry = new RedisStreamHandlerRegistry();
		registry.Add<OrderPaid>(_ => Task.CompletedTask, new RedisStreamOption { Group = "workers" });
		_database.StreamCreateConsumerGroupAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
			.Returns<bool>(true);
		_database.StreamReadGroupAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue?>(), Arg.Any<int?>(), Arg.Any<CommandFlags>())
			.Returns(Array.Empty<StreamEntry>());
		var consumer = BuildConsumer(registry, "app:");

		await consumer.StartAsync(default);
		await Task.Delay(100);
		await consumer.StopAsync(default);

		// StopAsync диспозит CTS: повторный стоп честно падает — блок отработки не выкинут
		await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => consumer.StopAsync(default));

		await _database.Received(1).StreamCreateConsumerGroupAsync(
			(RedisKey)"app:stream:OrderPaid", (RedisValue)"workers", StreamPosition.Beginning, true, Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task BusyGroup_is_swallowed()
	{
		var registry = new RedisStreamHandlerRegistry();
		registry.Add<OrderPaid>(_ => Task.CompletedTask, new RedisStreamOption());
		_database.StreamCreateConsumerGroupAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
			.Returns<bool>(_ => throw new RedisServerException("BUSYGROUP Consumer Group name already exists"));
		_database.StreamReadGroupAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue?>(), Arg.Any<int?>(), Arg.Any<CommandFlags>())
			.Returns(Array.Empty<StreamEntry>());
		var consumer = BuildConsumer(registry);

		await consumer.StartAsync(default);
		await Task.Delay(50);
		await consumer.StopAsync(default);

		await _database.Received(1).StreamCreateConsumerGroupAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Delivered_entries_reach_the_handler_and_get_acked()
	{
		var delivered = new List<int>();
		var registry = new RedisStreamHandlerRegistry();
		registry.Add<OrderPaid>(m => { delivered.Add(m.Amount); return Task.CompletedTask; }, new RedisStreamOption());
		var consumer = BuildConsumer(registry);

		await consumer.DeliverAsync((RedisKey)"stream:OrderPaid", Entry(7), typeof(OrderPaid),
			registry.Snapshot().Select(e => e.Handler).ToArray(), new RedisStreamOption());

		Assert.Equal([7], delivered);
		await _database.Received(1).StreamAcknowledgeAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), (RedisValue)"1-1", Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Failing_handler_retries_then_dead_letters()
	{
		var calls = 0;
		var registry = new RedisStreamHandlerRegistry();
		registry.Add<OrderPaid>(_ => { calls++; throw new InvalidOperationException("boom"); },
			new RedisStreamOption { MaxRetryCount = 2 });
		var consumer = BuildConsumer(registry);

		await consumer.DeliverAsync((RedisKey)"stream:OrderPaid", Entry(7), typeof(OrderPaid),
			registry.Snapshot().Select(e => e.Handler).ToArray(), new RedisStreamOption { MaxRetryCount = 2 });

		Assert.Equal(2, calls);
		var adds = _database.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "StreamAddAsync").ToList();
		var dlq = Assert.Single(adds);
		Assert.Equal((RedisKey)"stream:OrderPaid.dlq", (RedisKey)dlq.GetArguments()[0]!);

		// DLQ-поля: body — ИМЕННО поле body (не type), type скопирован, error заполнен
		var pairs = Assert.IsType<NameValueEntry[]>(dlq.GetArguments()[1]);
		var body = Assert.Single(pairs, p => p.Name == "body").Value.ToString();
		Assert.Contains("\"Amount\":7", body);
		Assert.Contains(pairs, p => p.Name == "type" && p.Value.ToString() == "OrderPaid");
		Assert.Contains(pairs, p => p.Name == "error" && p.Value.ToString().Length > 0);
		await _database.Received(1).StreamAcknowledgeAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), (RedisValue)"1-1", Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Poison_body_dead_letters_without_retries()
	{
		var calls = 0;
		var registry = new RedisStreamHandlerRegistry();
		registry.Add<OrderPaid>(_ => { calls++; return Task.CompletedTask; }, new RedisStreamOption());
		var consumer = BuildConsumer(registry);
		var poison = new StreamEntry((RedisValue)"1-1", [new NameValueEntry("body", (RedisValue)"not-json"u8.ToArray())]);

		await consumer.DeliverAsync((RedisKey)"stream:OrderPaid", poison, typeof(OrderPaid),
			registry.Snapshot().Select(e => e.Handler).ToArray(), new RedisStreamOption());

		Assert.Equal(0, calls);
		var adds = _database.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "StreamAddAsync").ToList();
		var dlq = Assert.Single(adds);
		Assert.Equal((RedisKey)"stream:OrderPaid.dlq", (RedisKey)dlq.GetArguments()[0]!);
	}

	[Fact]
	public void Stream_option_defaults()
	{
		var option = new RedisStreamOption();

		Assert.Equal("main", option.Group);
		Assert.Equal(3, option.MaxRetryCount);
		Assert.Equal(".dlq", option.DeadLetterStream);
		Assert.Equal(TimeSpan.FromMinutes(1), option.MinIdleBeforeReclaim);
		Assert.Equal(10, option.BatchSize);
		Assert.Equal(100_000, option.MaxStreamLength);
	}

	private static StackExchange.Redis.StreamAutoClaimResult Claim(params StreamEntry[] entries)
	{
		// единственный ctor — internal: строим через рефлексию, стабильнее, чем IVT в чужую сборку
		var ctor = typeof(StackExchange.Redis.StreamAutoClaimResult).GetConstructors(
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
		return (StackExchange.Redis.StreamAutoClaimResult)ctor.Invoke([(RedisValue)"0-0", entries, Array.Empty<RedisValue>()]);
	}

	private sealed record OrderPaid(int Amount);

	[Fact]
	public async Task Sweep_redelivers_claimed_entries()
	{
		var delivered = new List<int>();
		var registry = new RedisStreamHandlerRegistry();
		registry.Add<OrderPaid>(m => { delivered.Add(m.Amount); return Task.CompletedTask; }, new RedisStreamOption());
		var claimed = Claim(Entry(5));
		_database.StreamAutoClaimAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<long>(), Arg.Any<RedisValue>(), Arg.Any<int?>(), Arg.Any<CommandFlags>())
			.Returns(claimed);
		var consumer = BuildConsumer(registry);

		// зависшая запись, отжатая XAUTOCLAIM, идёт через тот же конвейер — дубликаты возможны (at-least-once)
		await consumer.SweepPendingAsync((RedisKey)"stream:OrderPaid", typeof(OrderPaid),
			registry.Snapshot().Select(e => e.Handler).ToArray(), new RedisStreamOption());

		Assert.Equal([5], delivered);
		await _database.Received(1).StreamAcknowledgeAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), (RedisValue)"1-1", Arg.Any<CommandFlags>());
		await _database.Received(1).StreamAutoClaimAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
			Arg.Any<long>(), (RedisValue)"0-0", Arg.Any<int?>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Sweep_without_claimed_entries_is_silent()
	{
		var calls = 0;
		var registry = new RedisStreamHandlerRegistry();
		registry.Add<OrderPaid>(_ => { calls++; return Task.CompletedTask; }, new RedisStreamOption());
		var empty = Claim();
		_database.StreamAutoClaimAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<long>(), Arg.Any<RedisValue>(), Arg.Any<int?>(), Arg.Any<CommandFlags>())
			.Returns(empty);
		var consumer = BuildConsumer(registry);

		await consumer.SweepPendingAsync((RedisKey)"s", typeof(OrderPaid),
			registry.Snapshot().Select(e => e.Handler).ToArray(), new RedisStreamOption());

		Assert.Equal(0, calls);
	}

	[Fact]
	public async Task AddRedisStreamHandler_registers_settings_and_handlers()
	{
		var services = new ServiceCollection();
		services.AddRedis(o => o.ConnectionString = "localhost:6379");
		services.AddRedisStreamHandler<OrderPaid>(_ => Task.CompletedTask, s => s.Group = "workers");
		services.AddRedisStreamHandler<OrderPaid>(_ => { });

		var registry = services.BuildServiceProvider().GetRequiredService<RedisStreamHandlerRegistry>();
		Assert.Equal(2, registry.Snapshot().Count);
		Assert.Equal("workers", registry.OptionFor(typeof(OrderPaid))!.Group);

		// sync-обёртка второго хендлера вызывается и доходит до действия
		var hit = false;
		services = new ServiceCollection();
		services.AddRedis(o => o.ConnectionString = "localhost:6379");
		services.AddRedisStreamHandler<OrderPaid>(m => hit = m.Amount == 7);
		var syncRegistry = services.BuildServiceProvider().GetRequiredService<RedisStreamHandlerRegistry>();
		await syncRegistry.Snapshot().Single().Handler(new OrderPaid(7));
		Assert.True(hit);
	}

	[Fact]
	public void AddRedisStreamHandler_rejects_null_handler()
	{
		Assert.Throws<ArgumentNullException>(() => new ServiceCollection()
			.AddRedisStreamHandler<OrderPaid>((Func<OrderPaid, Task>)null!));
	}

	[Fact]
	public void AddRedisStreamHandler_survives_without_add_redis()
	{
		var services = new ServiceCollection();

		services.AddRedisStreamHandler<OrderPaid>(_ => { });

		Assert.Single(services.Where(d => d.ServiceType == typeof(RedisStreamHandlerRegistry)));
	}

	[Fact]
	public async Task Disabled_dead_letter_skips_the_dlq_write()
	{
		var calls = 0;
		var settings = new RedisStreamOption { MaxRetryCount = 1, DeadLetterStream = "" };
		var registry = new RedisStreamHandlerRegistry();
		registry.Add<OrderPaid>(_ => { calls++; throw new InvalidOperationException("boom"); }, settings);
		var consumer = BuildConsumer(registry);

		await consumer.DeliverAsync((RedisKey)"stream:OrderPaid", Entry(7), typeof(OrderPaid),
			registry.Snapshot().Select(e => e.Handler).ToArray(), settings);

		Assert.Equal(1, calls);
		Assert.Empty(_database.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "StreamAddAsync"));
		await _database.Received(1).StreamAcknowledgeAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), (RedisValue)"1-1", Arg.Any<CommandFlags>());
	}

}
