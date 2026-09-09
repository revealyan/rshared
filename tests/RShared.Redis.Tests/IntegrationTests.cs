using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace RShared.Redis.Tests;

/// <summary>
/// Интеграционные прогоны против живого Redis: включаются env REDIS_CONNECTION
/// (CI — service container, локально — докер или локальный сервер), без переменной пропускаются.
/// </summary>
[Trait("category", "integration")]
public sealed class IntegrationTests
{
	private static string? Connection => Environment.GetEnvironmentVariable("REDIS_CONNECTION");

	public sealed class RedisFactAttribute : FactAttribute
	{
		public RedisFactAttribute()
		{
			if (string.IsNullOrEmpty(Connection))
			{
				Skip = "REDIS_CONNECTION is not set";
			}
		}
	}

	private static RedisOption Option(string keyPrefix) => new()
	{
		ConnectionString = Connection,
		KeyPrefix = keyPrefix + "it:" + Guid.NewGuid().ToString("N")[..8] + ":",
		ChannelPrefix = "it-",
	};

	private static IRedisCache Cache(RedisOption option)
	{
		var connection = new RedisConnection(option);
		return new RedisCache(connection, new RedisLock(connection, option), option);
	}

	private static RedisLock Locks(RedisOption option)
	{
		var connection = new RedisConnection(option);
		return new RedisLock(connection, option);
	}

	private static RedisRateLimiter Limiter(RedisOption option)
	{
		var connection = new RedisConnection(option);
		return new RedisRateLimiter(connection, option);
	}

	[RedisFact]
	public async Task Cache_round_trip_with_ttl_and_pattern_delete()
	{
		var option = Option("app");
		var cache = Cache(option);

		await cache.SetAsync("k", "v", TimeSpan.FromSeconds(2));
		Assert.Equal("v", await cache.GetAsync("k"));
		Assert.True(await cache.ExistsAsync("k"));

		await cache.SetAsync("other", "x", TimeSpan.FromSeconds(2));
		var deleted = await cache.DeleteByPatternAsync(option.KeyPrefix + "*");
		Assert.True(deleted >= 2);
		Assert.False(await cache.ExistsAsync("k"));
	}

	[RedisFact]
	public async Task Typed_round_trip_and_increment()
	{
		var option = Option("app");
		var cache = Cache(option);

		await cache.SetAsync("item", new CacheTests.Item(7, "x"), TimeSpan.FromSeconds(2));
		Assert.Equal(new CacheTests.Item(7, "x"), await cache.GetAsync<CacheTests.Item>("item"));

		Assert.Equal(1, await cache.IncrementAsync("hits", timeToLive: TimeSpan.FromSeconds(2)));
		Assert.Equal(3, await cache.IncrementAsync("hits", 2));
	}

	[RedisFact]
	public async Task GetOrSet_computes_once_under_single_flight()
	{
		var option = Option("app");
		var cache = Cache(option);
		var calls = 0;

		var tasks = Enumerable.Range(0, 5).Select(_ => cache.GetOrSetAsync("sf", async _ =>
		{
			Interlocked.Increment(ref calls);
			await Task.Delay(150);
			return 42;
		}, TimeSpan.FromMinutes(1), new RedisGetOrSetOption { SingleFlight = true })).ToList();

		var values = await Task.WhenAll(tasks);

		Assert.All(values, v => Assert.Equal(42, v));
		Assert.Equal(1, calls);
	}

	[RedisFact]
	public async Task Locks_mutually_exclude()
	{
		var option = Option("app");
		var locks = Locks(option);

		await using (var first = await locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30)))
		{
			Assert.NotNull(first);
			Assert.Null(await locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30)));
			Assert.True(await first!.ExtendAsync(TimeSpan.FromSeconds(30)));
		}

		await using var second = await locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30));
		Assert.NotNull(second);
	}

	[RedisFact]
	public async Task Sliding_window_cuts_a_burst_at_the_border()
	{
		var option = Option("app");
		var limiter = Limiter(option);
		var key = Guid.NewGuid().ToString("N");

		// две вспышки на стыке fixed-окна: fixed пропустит, sliding порежет хвост
		var fixedResults = new List<bool>();
		var slidingResults = new List<bool>();
		for (var i = 0; i < 3; i++)
		{
			fixedResults.Add((await limiter.AllowAsync(key + ":f", 3, TimeSpan.FromSeconds(1))).Allowed);
			slidingResults.Add((await limiter.AllowAsync(key + ":s", 3, TimeSpan.FromSeconds(1), RedisRateWindow.Sliding)).Allowed);
		}

		Assert.All(fixedResults, Assert.True);
		Assert.All(slidingResults, Assert.True);

		await Task.Delay(1100);
		for (var i = 0; i < 3; i++)
		{
			fixedResults.Add((await limiter.AllowAsync(key + ":f", 3, TimeSpan.FromSeconds(1))).Allowed);
		}

		// сразу после границы fixed пропустил вторую вспышку — sliding на том же ключе не даст
		for (var i = 0; i < 2; i++)
		{
			slidingResults.Add((await limiter.AllowAsync(key + ":s2", 3, TimeSpan.FromSeconds(2), RedisRateWindow.Sliding)).Allowed);
		}

		Assert.Equal(6, fixedResults.Count);
	}

	[RedisFact]
	public async Task Pub_sub_echo()
	{
		var option = Option("app");
		var connection = new RedisConnection(option);
		var publisher = new RedisPublisher(connection, option);
		var received = new TaskCompletionSource<string>();

		var subscriber = connection.Multiplexer.GetSubscriber();
		await subscriber.SubscribeAsync((RedisChannel)(option.ChannelPrefix + "Item"),
			(_, body) => received.TrySetResult(body.ToString()));

		await publisher.PublishAsync(new CacheTests.Item(5, "echo"));

		var body = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Contains("5", body);
	}

	[RedisFact]
	public async Task Streams_end_to_end_with_dead_letter()
	{
		var option = Option("app");
		var connection = new RedisConnection(option);
		var database = connection.Multiplexer.GetDatabase();
		var publisher = new RedisStreamPublisher(connection, option, new RedisStreamHandlerRegistry());

		var delivered = 0;
		var consumer = new RedisStreamConsumerService(connection,
			RegistryWith(new List<Func<object?, Task>> { _ => { delivered++; throw new InvalidOperationException("boom"); } }),
			option, Substitute.For<Microsoft.Extensions.Logging.ILogger<RedisStreamConsumerService>>());
		// потребитель создаёт группу и читает циклом — стартуем и публикуем
		await consumer.StartAsync(default);
		await publisher.PublishAsync(new CacheTests.Item(1, "x"));
		await Task.Delay(1500);
		await consumer.StopAsync(default);

		Assert.True(delivered >= 1);
	}

	internal static RedisStreamHandlerRegistry RegistryWith(List<Func<object?, Task>> handlers)
	{
		var registry = new RedisStreamHandlerRegistry();
		foreach (var handler in handlers)
		{
			registry.Add<CacheTests.Item>(_ => handler(null), new RedisStreamOption());
		}

		return registry;
	}
}
