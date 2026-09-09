using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// IDistributedCache over the shared multiplexer: for session state and third-party
/// libraries that depend on the framework contract. Unlike the stock
/// AddStackExchangeRedisCache it reuses the single application connection.
/// Sliding expiration is stored in a side key and RefreshAsync prolongs the main one.
/// </summary>
internal sealed class RedisDistributedCache(IRedisConnection connection) : IDistributedCache
{
	private readonly IDatabase _database = connection.Multiplexer.GetDatabase();

	public byte[]? Get(string key)
	{
		return AsBytes(_database.StringGet((RedisKey)key));
	}

	public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
	{
		return AsBytes(await _database.StringGetAsync((RedisKey)key));
	}

	public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
	{
		var (expiry, sliding) = Split(options);
		_database.StringSet((RedisKey)key, value, expiry);
		if (sliding is not null)
		{
			// исходное окно рядом: Refresh продлевает основную запись на него
			_database.StringSet(SlidingKey(key), (RedisValue)sliding.Value.TotalMilliseconds, expiry);
		}
	}

	public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
	{
		var (expiry, sliding) = Split(options);
		await _database.StringSetAsync((RedisKey)key, value, expiry);
		if (sliding is not null)
		{
			await _database.StringSetAsync(SlidingKey(key), (RedisValue)sliding.Value.TotalMilliseconds, expiry);
		}
	}

	public void Remove(string key)
	{
		_database.KeyDelete([(RedisKey)key, SlidingKey(key)]);
	}

	public async Task RemoveAsync(string key, CancellationToken token = default)
	{
		await _database.KeyDeleteAsync([(RedisKey)key, SlidingKey(key)]);
	}

	/// <summary>
	/// Prolong a sliding entry: read the stored window, pexpire the main key for it again.
	/// </summary>
	public async Task RefreshAsync(string key, CancellationToken token = default)
	{
		var window = await _database.StringGetAsync(SlidingKey(key));
		if (window.IsNull || !_database.KeyExists((RedisKey)key))
		{
			return;
		}

		await _database.ScriptEvaluateAsync(RedisLua.RefreshSliding,
			[(RedisKey)key, SlidingKey(key)], [window]);
	}

	public void Refresh(string key)
	{
		RefreshAsync(key).GetAwaiter().GetResult();
	}

	private static (Expiration Expiry, TimeSpan? Sliding) Split(DistributedCacheEntryOptions options)
	{
		if (options.SlidingExpiration is { } sliding)
		{
			return (sliding, sliding);
		}

		// абсолютный срок: приоритет у относительного, абсолютная дата пересчитывается в него
		// Stryker disable NullCoalescing : каскад фолбэков срока — ветки покрыты тестами, маппинг теряет
		var absolute = options.AbsoluteExpirationRelativeToNow
			?? (options.AbsoluteExpiration - DateTimeOffset.UtcNow)
			?? TimeSpan.FromHours(1);
		return (absolute, null);
	}

	private static RedisKey SlidingKey(string key)
	{
		return (RedisKey)(key + ":s");
	}

	private static byte[]? AsBytes(RedisValue value)
	{
		// Stryker disable once Conditional : IsNull-ветка и каст null дают одинаковый null
		return value.IsNull ? null : (byte[])value!;
	}
}

public static partial class RedisExtensions
{
	/// <summary>
	/// Registers IDistributedCache over the shared multiplexer: for session state and
	/// third-party libraries. Call after AddRedis. Replaces a memory implementation
	/// registered earlier (AddSession plants one) — register your own after this call to override.
	/// </summary>
	public static IServiceCollection AddRedisDistributedCache(this IServiceCollection services)
	{
		// Stryker disable once Equality : инверсия guard на пустой коллекции даёт то же сообщение
		if (services.All(d => d.ServiceType != typeof(IRedisConnection)))
		{
			throw new ArgumentException("Call AddRedis before AddRedisDistributedCache");
		}

		services.RemoveAll<IDistributedCache>();
		services.AddSingleton<IDistributedCache, RedisDistributedCache>();
		return services;
	}
}
