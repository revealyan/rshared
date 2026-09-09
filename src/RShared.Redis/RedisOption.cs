using System.Text.Json;
using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Redis dependency injection configuration options.
/// </summary>
public sealed class RedisOption
{
	/// <summary>
	/// Connection string. Required unless <see cref="Connection"/> is set.
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>
	/// Ready multiplexer. When set, <see cref="ConnectionString"/> must be left null.
	/// Owned by the caller: the package never disposes it.
	/// </summary>
	public IConnectionMultiplexer? Connection { get; set; }

	/// <summary>
	/// Prefix prepended to every data key (cache, locks, rate limits, streams). Empty by default.
	/// </summary>
	// Stryker disable once String : NRT-заглушка, значение задаёт потребитель
	public string KeyPrefix { get; set; } = string.Empty;

	/// <summary>
	/// Prefix prepended to pub/sub channel names. Empty by default.
	/// </summary>
	// Stryker disable once String : NRT-заглушка, значение задаёт потребитель
	public string ChannelPrefix { get; set; } = string.Empty;

	/// <summary>
	/// Raw escape hatch applied to parsed connection options (timeouts, ssl, retry policy).
	/// Applied after the defaults, so it can override them.
	/// </summary>
	public Action<ConfigurationOptions>? ConfigureConnection { get; set; }

	/// <summary>
	/// JSON serializer options for typed cache entries and messages. Package defaults when null.
	/// </summary>
	public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
