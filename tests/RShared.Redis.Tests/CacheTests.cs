using System.Text.Json;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace RShared.Redis.Tests;

/// <summary>
/// Кэш: соглашения о вызовах IDatabase (ключи с префиксом, TTL, NX-семантика GetOrSet),
/// инкремент Lua-скриптом, удаление по паттерну курсорами
/// </summary>
public sealed class CacheTests
{
	private readonly IConnectionMultiplexer _multiplexer;
	private readonly IDatabase _database;
	private readonly IRedisLock _locks;
	private readonly RedisOption _option = new() { ConnectionString = "localhost:6379" };

	public CacheTests()
	{
		(_ , _multiplexer, _database) = RedisMocks.Database();
		_locks = Substitute.For<IRedisLock>();
	}

	private RedisCache BuildConnected(string keyPrefix = "")
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);
		_option.KeyPrefix = keyPrefix;
		return new RedisCache(connection, _locks, _option);
	}

	[Fact]
	public async Task Get_returns_null_on_miss()
	{
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null);
		var cache = BuildConnected();

		Assert.Null(await cache.GetAsync("k"));
	}

	[Fact]
	public async Task Keys_carry_the_prefix()
	{
		_database.StringGetAsync(Arg.Any<RedisKey>()).Returns(RedisValue.Null);
		var cache = BuildConnected("app:");

		await cache.GetAsync("k");

		await _database.Received(1).StringGetAsync((RedisKey)"app:k");
	}

	[Fact]
	public async Task Set_stores_with_and_without_ttl()
	{
		var cache = BuildConnected();

		await cache.SetAsync("k", "v");
		await cache.SetAsync("k", "v", TimeSpan.FromMinutes(5));

		await _database.Received(1).StringSetAsync((RedisKey)"k", (RedisValue)"v", Expiration.Default);
		await _database.Received(1).StringSetAsync((RedisKey)"k", (RedisValue)"v", (Expiration)TimeSpan.FromMinutes(5));
	}

	[Fact]
	public async Task Delete_and_exists_map_to_key_operations()
	{
		_database.KeyDeleteAsync((RedisKey)"k").Returns(true);
		_database.KeyExistsAsync((RedisKey)"k").Returns(false, true);
		var cache = BuildConnected();

		Assert.True(await cache.DeleteAsync("k"));
		Assert.False(await cache.ExistsAsync("k"));
		Assert.True(await cache.ExistsAsync("k"));
	}

	[Fact]
	public async Task Typed_get_deserializes_the_payload()
	{
		var payload = JsonSerializer.SerializeToUtf8Bytes(new Item(42, "x"));
		_database.StringGetAsync((RedisKey)"k").Returns((RedisValue)payload);
		var cache = BuildConnected();

		var item = await cache.GetAsync<Item>("k");

		Assert.Equal(new Item(42, "x"), item);
	}

	[Fact]
	public async Task Typed_get_miss_returns_default()
	{
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null);
		var cache = BuildConnected();

		Assert.Null(await cache.GetAsync<Item>("k"));
	}

	[Fact]
	public async Task Typed_set_serializes_the_payload()
	{
		var cache = BuildConnected();

		await cache.SetAsync("k", new Item(42, "x"), TimeSpan.FromMinutes(1));

		await _database.Received(1).StringSetAsync(
			(RedisKey)"k",
			Arg.Is<RedisValue>(v => v.ToString().Contains("\"Number\":42")),
			TimeSpan.FromMinutes(1));
	}

	[Fact]
	public async Task GetOrSet_hit_returns_stored_without_factory()
	{
		var payload = JsonSerializer.SerializeToUtf8Bytes(new Item(7, "hit"));
		_database.StringGetAsync((RedisKey)"k").Returns((RedisValue)payload);
		var cache = BuildConnected();
		var calls = 0;

		var item = await cache.GetOrSetAsync("k", _ => { calls++; return Task.FromResult(new Item(1, "own")); }, TimeSpan.FromMinutes(1));

		Assert.Equal(new Item(7, "hit"), item);
		Assert.Equal(0, calls);
	}

	[Fact]
	public async Task GetOrSet_miss_computes_and_sets_with_nx()
	{
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null);
		_database.StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(), Arg.Any<Expiration>(), Arg.Any<ValueCondition>())
			.Returns(true);
		var cache = BuildConnected();

		var item = await cache.GetOrSetAsync("k", _ => Task.FromResult(new Item(1, "own")), TimeSpan.FromMinutes(1));

		Assert.Equal(new Item(1, "own"), item);

		// форма с условием (5 аргументов) = SET NX: ключ совпадает
		var sets = _database.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "StringSetAsync").ToList();
		Assert.Contains(sets, c => c.GetArguments()[0]?.ToString() == "k");
	}

	[Fact]
	public async Task GetOrSet_lost_nx_returns_the_winner()
	{
		var winner = JsonSerializer.SerializeToUtf8Bytes(new Item(9, "winner"));
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null, (RedisValue)winner);
		_database.StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(), Arg.Any<Expiration>(), Arg.Any<ValueCondition>())
			.Returns(false);
		var cache = BuildConnected();

		var item = await cache.GetOrSetAsync("k", _ => Task.FromResult(new Item(1, "own")), TimeSpan.FromMinutes(1));

		Assert.Equal(new Item(9, "winner"), item);
	}

	[Fact]
	public async Task GetOrSet_lost_nx_and_expired_winner_returns_own()
	{
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null);
		_database.StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(), Arg.Any<Expiration>(), Arg.Any<ValueCondition>())
			.Returns(false);
		var cache = BuildConnected();

		var item = await cache.GetOrSetAsync("k", _ => Task.FromResult(new Item(1, "own")), TimeSpan.FromMinutes(1));

		Assert.Equal(new Item(1, "own"), item);
	}

	[Fact]
	public async Task Increment_runs_the_lua_script()
	{
		_database.ScriptEvaluateAsync(RedisLua.Increment, Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
			.Returns(RedisResult.Create(7L));
		var cache = BuildConnected("app:");

		var value = await cache.IncrementAsync("hits", 2, TimeSpan.FromMinutes(1));

		Assert.Equal(7, value);
		RedisKey[] keys = ["app:hits"];
		RedisValue[] args = [2L, 60000L];
		await _database.Received(1).ScriptEvaluateAsync(RedisLua.Increment,
			Arg.Is<RedisKey[]>(k => k.SequenceEqual(keys)),
			Arg.Is<RedisValue[]>(v => v.SequenceEqual(args)));
	}

	[Fact]
	public async Task Increment_without_ttl_passes_no_expiry()
	{
		_database.ScriptEvaluateAsync(RedisLua.Increment, Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
			.Returns(RedisResult.Create(1L));
		var cache = BuildConnected();

		await cache.IncrementAsync("hits");

		RedisValue[] args = [1L, -1L];
		await _database.Received(1).ScriptEvaluateAsync(RedisLua.Increment,
			Arg.Any<RedisKey[]>(),
			Arg.Is<RedisValue[]>(v => v.SequenceEqual(args)));
	}

	[Fact]
	public async Task DeleteByPattern_scans_and_unlinks_in_batches()
	{
		var keys = new RedisKey[] { "a:1", "a:2", "a:3", "b:4" };
		var server = Substitute.For<IServer>();
		var endpoint = new System.Net.DnsEndPoint("localhost", 6379);
		_multiplexer.GetEndPoints().Returns([endpoint]);
		_multiplexer.GetServer(endpoint, Arg.Any<object?>()).Returns(server);
		server.KeysAsync(-1, (RedisValue)"a:*", 2, Arg.Any<long>(), Arg.Any<int>(), CommandFlags.None).Returns(new RedisMocks.KeySource(keys[0], keys[1], keys[2]));
		_database.KeyDeleteAsync(Arg.Any<RedisKey[]>()).Returns(2L, 1L);
		var cache = BuildConnected();

		var deleted = await cache.DeleteByPatternAsync("a:*", batchSize: 2);

		server.Received(1).KeysAsync(-1, (RedisValue)"a:*", 2, Arg.Any<long>(), Arg.Any<int>(), CommandFlags.None);
		// полный батч флэшнулся в цикле + хвост из одного ключа после скана
		Assert.Equal(3, deleted);
		RedisKey[] full = [keys[0], keys[1]];
		RedisKey[] tail = [keys[2]];
		await _database.Received(1).KeyDeleteAsync(Arg.Is<RedisKey[]>(k => k.SequenceEqual(full)));
		await _database.Received(1).KeyDeleteAsync(Arg.Is<RedisKey[]>(k => k.SequenceEqual(tail)));
	}

	[Fact]
	public async Task DeleteByPattern_passes_the_full_pattern_to_the_scan()
	{
		var server = Substitute.For<IServer>();
		var endpoint = new System.Net.DnsEndPoint("localhost", 6379);
		_multiplexer.GetEndPoints().Returns([endpoint]);
		_multiplexer.GetServer(endpoint, Arg.Any<object?>()).Returns(server);
		server.KeysAsync(-1, (RedisValue)"app:*", Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), CommandFlags.None).Returns(new RedisMocks.KeySource());
		var cache = BuildConnected("app:");

		await cache.DeleteByPatternAsync("app:*");

		await _database.DidNotReceiveWithAnyArgs().KeyDeleteAsync(Arg.Any<RedisKey[]>());
	}

	internal sealed record Item(int Number, string Text);

	[Fact]
	public async Task GetOrSet_single_flight_waits_then_double_checks()
	{
		var winner = JsonSerializer.SerializeToUtf8Bytes(new Item(3, "locked"));
		// первый GET — промах; после лока — победитель уже записал
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null, (RedisValue)winner);
		var leaseDb = Substitute.For<IDatabase>();
		var lease = new RedisLease(leaseDb, "sf", "t");
		_locks.WaitAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(lease);
		var cache = BuildConnected();
		var calls = 0;

		var item = await cache.GetOrSetAsync("k", _ => { calls++; return Task.FromResult(new Item(1, "own")); },
			TimeSpan.FromMinutes(1), new RedisGetOrSetOption { SingleFlight = true });

		// double-check сработал: фабрика не звалась, вернулся экземпляр победителя
		Assert.Equal(new Item(3, "locked"), item);
		Assert.Equal(0, calls);

		// лок снят после выхода
		await leaseDb.Received(1).ScriptEvaluateAsync(RedisLua.ReleaseLock,
			Arg.Is<RedisKey[]>(k => k[0] == (RedisKey)"sf"), Arg.Any<RedisValue[]>());
		await _locks.Received(1).WaitAsync("sf:k", Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetOrSet_single_flight_computes_under_the_lock()
	{
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null);
		var leaseDb = Substitute.For<IDatabase>();
		var lease = new RedisLease(leaseDb, "sf", "t");
		_locks.WaitAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(lease);
		_database.StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(), Arg.Any<Expiration>(), Arg.Any<ValueCondition>())
			.Returns(true);
		var cache = BuildConnected();

		var item = await cache.GetOrSetAsync("k", _ => Task.FromResult(new Item(1, "own")),
			TimeSpan.FromMinutes(1), new RedisGetOrSetOption { SingleFlight = true });

		Assert.Equal(new Item(1, "own"), item);

		// finally-релиз лока после своего вычисления
		await leaseDb.Received(1).ScriptEvaluateAsync(RedisLua.ReleaseLock,
			Arg.Is<RedisKey[]>(k => k[0] == (RedisKey)"sf"), Arg.Any<RedisValue[]>());
	}

	[Fact]
	public async Task GetOrSet_single_flight_lock_timeout_degrades_to_concurrent()
	{
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null);
		_locks.WaitAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns((RedisLease?)null);
		_database.StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(), Arg.Any<Expiration>(), Arg.Any<ValueCondition>())
			.Returns(true);
		var cache = BuildConnected();

		var item = await cache.GetOrSetAsync("k", _ => Task.FromResult(new Item(1, "own")),
			TimeSpan.FromMinutes(1), new RedisGetOrSetOption { SingleFlight = true });

		// лока не дождались — вычислили сами, это деградация, а не ошибка
		Assert.Equal(new Item(1, "own"), item);
	}

	[Fact]
	public async Task GetOrSet_single_flight_miss_then_own_set()
	{
		// GET-промах и после лока тоже промах — вычисляем и записываем, релиз лока в finally
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null);
		var lease = new RedisLease(Substitute.For<IDatabase>(), "sf", "t");
		_locks.WaitAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(lease);
		_database.StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(), Arg.Any<Expiration>(), Arg.Any<ValueCondition>())
			.Returns(true);
		var cache = BuildConnected();

		var item = await cache.GetOrSetAsync("k", _ => Task.FromResult(new Item(1, "own")),
			TimeSpan.FromMinutes(1), new RedisGetOrSetOption { SingleFlight = true });

		Assert.Equal(new Item(1, "own"), item);
	}

	[Fact]
	public void Null_payload_throws_json_exception()
	{
		var exception = Assert.Throws<JsonException>(() => RedisJson.Deserialize<Item>("null"u8.ToArray()));

		Assert.Contains("Payload", exception.Message);
	}

}
