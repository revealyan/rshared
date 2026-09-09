using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace RShared.Redis.Tests;

/// <summary>
/// IDistributedCache-адаптер: get/set/remove в байтах, sliding через side-key,
/// Refresh Lua-продлением, регистрации (снимает чужую memory-реализацию, fail-fast без AddRedis)
/// </summary>
public sealed class DistributedCacheTests
{
	private readonly IConnectionMultiplexer _multiplexer;
	private readonly IDatabase _database;

	public DistributedCacheTests()
	{
		(_, _multiplexer, _database) = RedisMocks.Database();
	}

	private RedisDistributedCache Build()
	{
		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(_multiplexer);
		return new RedisDistributedCache(connection);
	}

	[Fact]
	public async Task Get_miss_returns_null_and_hit_returns_bytes()
	{
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null, (RedisValue)"v"u8.ToArray());
		var cache = Build();

		Assert.Null(await cache.GetAsync("k"));
		Assert.Equal("v"u8.ToArray(), await cache.GetAsync("k"));
	}

	[Fact]
	public async Task Set_absolute_writes_one_key_with_expiry()
	{
		var cache = Build();

		await cache.SetAsync("k", "v"u8.ToArray(), new DistributedCacheEntryOptions
		{
			AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
		});

		await _database.Received(1).StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(), Arg.Any<Expiration>());

		// side-key для абсолютного срока не пишется
		var sets = _database.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "StringSetAsync")
			.Select(c => c.GetArguments()[0]?.ToString())
			.ToList();
		Assert.DoesNotContain("k:s", sets);
	}

	[Fact]
	public async Task Set_sliding_writes_the_side_key_with_the_window()
	{
		var cache = Build();

		await cache.SetAsync("k", "v"u8.ToArray(), new DistributedCacheEntryOptions
		{
			SlidingExpiration = TimeSpan.FromMinutes(2),
		});

		await _database.Received(1).StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(), (Expiration)TimeSpan.FromMinutes(2));
		await _database.Received(1).StringSetAsync((RedisKey)"k:s", (RedisValue)120000D, (Expiration)TimeSpan.FromMinutes(2));
	}

	[Fact]
	public async Task Remove_deletes_both_keys()
	{
		var cache = Build();
		RedisKey[] removed = ["k", "k:s"];

		await cache.RemoveAsync("k");

		await _database.Received(1).KeyDeleteAsync(Arg.Is<RedisKey[]>(k => k.SequenceEqual(removed)));
	}

	[Fact]
	public async Task Refresh_prolongs_by_the_stored_window()
	{
		_database.StringGetAsync((RedisKey)"k:s").Returns((RedisValue)120000D);
		_database.KeyExists((RedisKey)"k").Returns(true);
		var cache = Build();

		await cache.RefreshAsync("k");

		RedisKey[] keys = ["k", "k:s"];
		await _database.Received(1).ScriptEvaluateAsync(RedisLua.RefreshSliding,
			Arg.Is<RedisKey[]>(k => k.SequenceEqual(keys)), Arg.Any<RedisValue[]>());
	}

	[Fact]
	public async Task Refresh_without_side_key_does_nothing()
	{
		_database.StringGetAsync((RedisKey)"k:s").Returns(RedisValue.Null);
		var cache = Build();

		await cache.RefreshAsync("k");

		await _database.DidNotReceiveWithAnyArgs().ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>());
	}

	[Fact]
	public void Sync_wrappers_map_to_the_same_operations()
	{
		_database.StringGetAsync((RedisKey)"k").Returns(RedisValue.Null);
		_database.StringGetAsync((RedisKey)"k:s").Returns((RedisValue)120000D);
		_database.KeyExists((RedisKey)"k").Returns(true);
		var cache = Build();

		Assert.Null(cache.Get("k"));
		cache.Set("k", "v"u8.ToArray(), new DistributedCacheEntryOptions
		{
			SlidingExpiration = TimeSpan.FromMinutes(2),
		});
		cache.Set("k2", "v"u8.ToArray(), new DistributedCacheEntryOptions
		{
			AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
		});
		cache.Refresh("k");
		cache.Remove("k");

		_database.Received(1).StringGet((RedisKey)"k");
		_database.Received(1).StringSet((RedisKey)"k", Arg.Any<RedisValue>(), Arg.Any<Expiration>());
		_database.Received(1).StringSet((RedisKey)"k2", Arg.Any<RedisValue>(), Arg.Any<Expiration>());

		// sliding-запись пишет side-key с исходным окном и синхронно
		_database.Received(1).StringSet((RedisKey)"k:s", (RedisValue)120000D, Arg.Any<Expiration>());
		_database.Received(1).ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>());
		_database.Received(1).KeyDelete(Arg.Any<RedisKey[]>());
	}

	[Fact]
	public void Registration_requires_add_redis_first()
	{
		var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRedisDistributedCache());

		Assert.Contains("AddRedis", exception.Message);
	}

	[Fact]
	public async Task Registration_replaces_the_memory_implementation()
	{
		var services = new ServiceCollection();
		services.AddDistributedMemoryCache();
		services.AddRedis(o => o.ConnectionString = "localhost:6379");
		services.AddRedisDistributedCache();

		await using var provider = services.BuildServiceProvider();

		Assert.IsType<RedisDistributedCache>(provider.GetRequiredService<IDistributedCache>());

		// memory-реализация снята RemoveAll, а не задавлена последней регистрацией
		using var scope = provider.CreateScope();
		Assert.Single(scope.ServiceProvider.GetServices<IDistributedCache>());
	}

	[Fact]
	public async Task Set_absolute_date_recomputes_the_expiry()
	{
		var cache = Build();

		await cache.SetAsync("k", "v"u8.ToArray(), new DistributedCacheEntryOptions
		{
			AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(5),
		});

		// абсолютная дата пересчитана в относительный срок ~300000 мс (допуск на конверсию)
		await _database.Received(1).StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(),
			Arg.Is<Expiration>(e => System.Text.RegularExpressions.Regex.IsMatch(e.ToString() ?? "", @"PX 2999\d\d|PX 3000\d\d")));
	}

	[Fact]
	public async Task Set_without_any_expiry_falls_back_to_an_hour()
	{
		var cache = Build();

		await cache.SetAsync("k", "v"u8.ToArray(), new DistributedCacheEntryOptions());

		// без срока пакет держит запись час — документированное поведение фолбэка
		await _database.Received(1).StringSetAsync((RedisKey)"k", Arg.Any<RedisValue>(), (Expiration)TimeSpan.FromHours(1));
	}

	[Fact]
	public async Task Refresh_without_the_main_key_does_nothing()
	{
		_database.StringGetAsync((RedisKey)"k:s").Returns((RedisValue)120000D);
		_database.KeyExists((RedisKey)"k").Returns(false);
		var cache = Build();

		await cache.RefreshAsync("k");

		await _database.DidNotReceiveWithAnyArgs().ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>());
	}
}
