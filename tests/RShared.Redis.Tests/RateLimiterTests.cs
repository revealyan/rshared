using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace RShared.Redis.Tests;

/// <summary>
/// Лимитер: Lua-контракты обоих окон, ключи с префиксом, валидация аргументов
/// </summary>
public sealed class RateLimiterTests
{
	private readonly IConnectionMultiplexer _multiplexer;
	private readonly IDatabase _database;

	public RateLimiterTests()
	{
		(_, _multiplexer, _database) = RedisMocks.Database();
	}

	private RedisRateLimiter Build(string keyPrefix = "")
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);
		return new RedisRateLimiter(connection, new RedisOption { ConnectionString = "x", KeyPrefix = keyPrefix });
	}

	private void SetScriptResult(params long[] parts)
	{
		var result = RedisResult.Create(parts.Select(p => RedisResult.Create(p)).ToArray());
		_database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
			.Returns(result);
	}

	[Fact]
	public async Task Fixed_allows_within_the_limit()
	{
		SetScriptResult(2, 58000);
		var limiter = Build("app:");

		var result = await limiter.AllowAsync("login", limit: 5, window: TimeSpan.FromMinutes(1));

		Assert.True(result.Allowed);
		Assert.Equal(3, result.Remaining);
		Assert.Equal(TimeSpan.Zero, result.RetryAfter);
		RedisKey[] keys = ["app:rate:login"];
		RedisValue[] args = [60000L];
		await _database.Received(1).ScriptEvaluateAsync(RedisLua.FixedWindow,
			Arg.Is<RedisKey[]>(k => k.SequenceEqual(keys)),
			Arg.Is<RedisValue[]>(v => v.SequenceEqual(args)));
	}

	[Fact]
	public async Task Fixed_denies_over_the_limit_with_retry_after()
	{
		SetScriptResult(6, 42000);
		var limiter = Build();

		var result = await limiter.AllowAsync("login", limit: 5, window: TimeSpan.FromMinutes(1));

		Assert.False(result.Allowed);
		Assert.Equal(0, result.Remaining);
		Assert.Equal(TimeSpan.FromMilliseconds(42000), result.RetryAfter);
	}

	[Fact]
	public async Task Sliding_allows_within_the_limit()
	{
		SetScriptResult(1, 4, 0);
		var limiter = Build("app:");

		var result = await limiter.AllowAsync("login", limit: 5, window: TimeSpan.FromMinutes(1), RedisRateWindow.Sliding);

		Assert.True(result.Allowed);
		Assert.Equal(4, result.Remaining);
		await _database.Received(1).ScriptEvaluateAsync(RedisLua.SlidingWindow,
			Arg.Is<RedisKey[]>(k => k[0] == (RedisKey)"app:rate:login" && k.Length == 1),
			Arg.Is<RedisValue[]>(a => a.Length == 3 && (long)a[0] == 60000L && (long)a[1] == 5L
				&& a[2].ToString()!.Length == 32));
	}

	[Fact]
	public async Task Sliding_allows_up_to_and_including_the_limit()
	{
		SetScriptResult(1, 4, 0);
		var limiter = Build();

		var result = await limiter.AllowAsync("k", limit: 5, window: TimeSpan.FromMinutes(1), RedisRateWindow.Sliding);

		// count == limit (первый элемент {1,...} — allowed=1): граница включительно
		Assert.True(result.Allowed);
	}

	[Fact]
	public async Task Sliding_denies_with_retry_after_from_the_oldest_hit()
	{
		SetScriptResult(0, 0, 17000);
		var limiter = Build();

		var result = await limiter.AllowAsync("login", limit: 5, window: TimeSpan.FromMinutes(1), RedisRateWindow.Sliding);

		Assert.False(result.Allowed);
		Assert.Equal(TimeSpan.FromMilliseconds(17000), result.RetryAfter);
	}

	[Fact]
	public async Task Nonpositive_limit_throws()
	{
		var limiter = Build();

		await Assert.ThrowsAsync<ArgumentException>(() => limiter.AllowAsync("k", 0, TimeSpan.FromMinutes(1)));
	}

	[Fact]
	public async Task Unit_limit_is_valid()
	{
		SetScriptResult(1, 58000);
		var limiter = Build();

		var result = await limiter.AllowAsync("k", limit: 1, window: TimeSpan.FromMinutes(1));

		Assert.True(result.Allowed);
	}

	[Fact]
	public async Task Error_messages_name_the_argument()
	{
		var limiter = Build();

		var limitEx = await Assert.ThrowsAsync<ArgumentException>(() => limiter.AllowAsync("k", 0, TimeSpan.FromMinutes(1)));
		var windowEx = await Assert.ThrowsAsync<ArgumentException>(() => limiter.AllowAsync("k", 5, TimeSpan.Zero));

		Assert.Contains("Limit", limitEx.Message);
		Assert.Contains("Window", windowEx.Message);
	}

	[Fact]
	public async Task Nonpositive_window_throws()
	{
		var limiter = Build();

		await Assert.ThrowsAsync<ArgumentException>(() => limiter.AllowAsync("k", 5, TimeSpan.Zero));
	}
}
