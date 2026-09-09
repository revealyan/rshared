using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace RShared.Redis.Tests;

/// <summary>
/// Локи: acquire через SET NX с TTL и токеном, release/extend Lua по токену,
/// идемпотентность диспоза, Wait опросом с таймаутом
/// </summary>
public sealed class LockTests
{
	private readonly IConnectionMultiplexer _multiplexer;
	private readonly IDatabase _database;

	public LockTests()
	{
		(_, _multiplexer, _database) = RedisMocks.Database();
	}

	private RedisLock Build(string keyPrefix = "")
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);
		return new RedisLock(connection, new RedisOption { ConnectionString = "x", KeyPrefix = keyPrefix });
	}

	private void SetAcquire(params bool[] outcomes)
	{
		_database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.NotExists)
			.Returns(outcomes[0], outcomes[1..]);
	}

	[Fact]
	public async Task TryAcquire_wins_with_nx_and_ttl()
	{
		SetAcquire(true);
		var locks = Build("app:");

		var lease = await locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30));

		Assert.NotNull(lease);
		await _database.Received(1).StringSetAsync(
			(RedisKey)"app:lock:job", Arg.Any<RedisValue>(), TimeSpan.FromSeconds(30), When.NotExists);
	}

	[Fact]
	public async Task TryAcquire_lost_returns_null()
	{
		SetAcquire(false);
		var locks = Build();

		Assert.Null(await locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30)));
	}

	[Fact]
	public async Task Dispose_releases_by_token_lua()
	{
		SetAcquire(true);
		var locks = Build("app:");
		var lease = await locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30));

		await lease!.DisposeAsync();

		await _database.Received(1).ScriptEvaluateAsync(RedisLua.ReleaseLock,
			Arg.Is<RedisKey[]>(k => k[0] == (RedisKey)"app:lock:job"),
			Arg.Is<RedisValue[]>(a => a.Length == 1 && a[0].ToString()!.Length == 32));
	}

	[Fact]
	public async Task Dispose_is_idempotent()
	{
		SetAcquire(true);
		var locks = Build();
		var lease = await locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30));

		await lease!.DisposeAsync();
		await lease.DisposeAsync();

		await _database.Received(1).ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>());
	}

	[Fact]
	public async Task Extend_true_while_held()
	{
		SetAcquire(true);
		_database.ScriptEvaluateAsync(RedisLua.ExtendLease, Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
			.Returns(RedisResult.Create(1));
		var locks = Build("app:");
		var lease = await locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30));

		Assert.True(await lease!.ExtendAsync(TimeSpan.FromMinutes(1)));
		await _database.Received(1).ScriptEvaluateAsync(RedisLua.ExtendLease,
			Arg.Is<RedisKey[]>(k => k[0] == (RedisKey)"app:lock:job"),
			Arg.Is<RedisValue[]>(a => a.Length == 2 && a[1] == (RedisValue)60000L));
	}

	[Fact]
	public async Task Extend_false_when_lost()
	{
		SetAcquire(true);
		_database.ScriptEvaluateAsync(RedisLua.ExtendLease, Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
			.Returns(RedisResult.Create(0));
		var locks = Build();
		var lease = await locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30));

		Assert.False(await lease!.ExtendAsync(TimeSpan.FromMinutes(1)));
	}

	[Fact]
	public void Sync_dispose_releases_once()
	{
		SetAcquire(true);
		_database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
			.Returns(RedisResult.Create(1));
		var locks = Build();

		var lease = locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
		lease!.Dispose();
		lease.Dispose();

		_database.Received(1).ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>());
	}

	[Fact]
	public async Task Wait_times_out_when_always_held()
	{
		SetAcquire(false);
		var locks = Build();

		var lease = await locks.WaitAsync("job", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(150));

		Assert.Null(lease);
	}

	[Fact]
	public async Task Wait_acquires_when_released()
	{
		SetAcquire(false, true);
		var locks = Build();

		var lease = await locks.WaitAsync("job", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

		Assert.NotNull(lease);
	}

	[Fact]
	public async Task Wait_with_zero_timeout_tries_once_and_stops()
	{
		SetAcquire(false);
		var locks = Build();

		// дедлайн 0: первая попытка до проверки дедлайна, после промаха — сразу null без опроса
		var lease = await locks.WaitAsync("job", TimeSpan.FromSeconds(30), TimeSpan.Zero);

		Assert.Null(lease);
		await _database.Received(1).StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>());
	}

	[Fact]
	public async Task Wait_polls_until_the_deadline()
	{
		SetAcquire(false, false, true);
		var locks = Build();

		var lease = await locks.WaitAsync("job", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

		// два промаха, третья попытка успешна — интервал опроса живой
		Assert.NotNull(lease);
		await _database.Received(3).StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>());
	}
}
