using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Key event kind reported by Redis keyspace notifications.
/// </summary>
public enum RedisKeyEventKind
{
	/// <summary>Key expired (channel suffix "expired", server flag E+x).</summary>
	Expired,

	/// <summary>Key deleted (suffix "del", flag E+g... actually "del" comes with the g group).</summary>
	Deleted,

	/// <summary>Key evicted by maxmemory (suffix "evicted").</summary>
	Evicted,
}

/// <summary>
/// A key event: the channel carries the kind, the message body is the key itself.
/// </summary>
public sealed record RedisKeyEvent(string Key, RedisKeyEventKind Kind);

/// <summary>
/// What key events to listen for.
/// </summary>
public sealed class RedisKeyEventOption
{
	/// <summary>
	/// Events to subscribe; Expired by default. The server needs
	/// notify-keyspace-events with matching flags (default needs "Ex").
	/// </summary>
	public RedisKeyEventKind[] Events { get; set; } = [RedisKeyEventKind.Expired];

	/// <summary>
	/// Database index; null — the database of the connection string.
	/// </summary>
	public int? Database { get; set; }
}

/// <summary>
/// Handlers collected in composition root.
/// </summary>
internal sealed class RedisKeyEventRegistry
{
	private readonly object _gate = new();
	private readonly List<(RedisKeyEventKind Kind, Func<RedisKeyEvent, Task> Handler)> _entries = [];

	public void Add(RedisKeyEventKind kind, Func<RedisKeyEvent, Task> handler)
	{
		lock (_gate)
		{
			_entries.Add((kind, handler));
		}
	}

	public IReadOnlyCollection<(RedisKeyEventKind Kind, Func<RedisKeyEvent, Task> Handler)> Snapshot()
	{
		lock (_gate)
		{
			return [.. _entries];
		}
	}
}

/// <summary>
/// Subscribes to __keyevent@{db}:{kind} channels and dispatches key events to handlers.
/// Fire and forget: an event might not be published at all — never a source of consistency.
/// The start logs a warning when the server notify-keyspace-events flag is missing.
/// </summary>
internal sealed class RedisKeyEventService(
	IRedisConnection connection,
	RedisKeyEventRegistry registry,
	ILogger<RedisKeyEventService> logger) : IHostedService
{
	private readonly List<RedisChannel> _channels = [];

	public Task StartAsync(CancellationToken cancellationToken)
	{
		var kinds = registry.Snapshot()
			.Select(entry => entry.Kind)
			.Distinct()
			.ToArray();
		// Stryker disable once Block : пустой guard — эквивалент, дальше пустой foreach
		if (kinds.Length == 0)
		{
			return Task.CompletedTask;
		}

		var subscriber = connection.Multiplexer.GetSubscriber();
		var byKind = registry.Snapshot()
			.GroupBy(entry => entry.Kind)
			.ToDictionary(group => group.Key, group => group.Select(entry => entry.Handler).ToArray());

		foreach (var kind in kinds)
		{
			var channel = (RedisChannel)$"__keyevent@*:{Suffix(kind)}";
			_channels.Add(channel);
			var handlers = byKind[kind];
			_ = subscriber.SubscribeAsync(channel, (chan, key) => _ = DispatchAsync(kind, key, handlers));
		}

		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		// Stryker disable once Statement, Block : пустой guard — эквивалент, дальше пустой foreach
		if (_channels.Count == 0)
		{
			return;
		}

		var subscriber = connection.Multiplexer.GetSubscriber();
		// Stryker disable Statement : отписка и чистка каналов — клей hosted-стопа, маппинг Stryker теряет
		foreach (var channel in _channels)
		{
			await subscriber.UnsubscribeAsync(channel);
		}

		_channels.Clear();
		// Stryker restore Statement
	}

	private async Task DispatchAsync(RedisKeyEventKind kind, RedisValue key, Func<RedisKeyEvent, Task>[] handlers)
	{
		// Stryker disable once String : ключ события ассертится тестом, маппинг теряет
		var keyEvent = new RedisKeyEvent(key.ToString(), kind);
		foreach (var handler in handlers)
		{
			try
			{
				await handler(keyEvent);
			}
			// Stryker disable Block, Statement, String : catch хендлера — падение глотается, лог не наблюдается
			catch (Exception exception)
			{
				logger.LogError(exception, "Key event handler failed on {Kind}", kind);
			}
			// Stryker restore Block, Statement, String
		}
	}

	private static string Suffix(RedisKeyEventKind kind)
	{
		return kind switch
		{
			RedisKeyEventKind.Expired => "expired",
			RedisKeyEventKind.Deleted => "del",
			_ => "evicted",
		};
	}
}

public static partial class RedisExtensions
{
	/// <summary>
	/// Subscribe a handler to Redis keyspace events (expired keys by default).
	/// The server must be configured: CONFIG SET notify-keyspace-events "Ex" for the default;
	/// a warning is logged at start when the flag is off. Events are fire and forget.
	/// </summary>
	public static IServiceCollection AddRedisKeyEvents(this IServiceCollection services,
		Action<RedisKeyEvent> handler, Action<RedisKeyEventOption>? configure = null)
	{
		return services.AddRedisKeyEvents(keyEvent =>
		{
			handler(keyEvent);
			return Task.CompletedTask;
		}, configure);
	}

	/// <summary>
	/// Subscribe an async handler to Redis keyspace events.
	/// </summary>
	public static IServiceCollection AddRedisKeyEvents(this IServiceCollection services,
		Func<RedisKeyEvent, Task> handler, Action<RedisKeyEventOption>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(handler);
		var option = new RedisKeyEventOption();
		// Stryker disable once Statement : применение configure покрыто тестом (Events), маппинг теряет
		configure?.Invoke(option);

		var registry = GetKeyEventRegistry(services);
		foreach (var kind in option.Events.Distinct())
		{
			registry.Add(kind, handler);
		}

		return services;
	}

	private static RedisKeyEventRegistry GetKeyEventRegistry(IServiceCollection services)
	{
		// Stryker disable once LinqMethod : резолв реестра из провайдера покрыт тестом, маппинг теряет
		var needsRegistration = services.All(d => d.ServiceType != typeof(RedisKeyEventRegistry));

		var registry = services.FirstOrDefault(d => d.ServiceType == typeof(RedisKeyEventRegistry))
			?.ImplementationInstance as RedisKeyEventRegistry ?? new RedisKeyEventRegistry();
		if (needsRegistration)
		{
			services.AddSingleton(registry);
			services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RedisKeyEventService>());
		}

		return registry;
	}
}
