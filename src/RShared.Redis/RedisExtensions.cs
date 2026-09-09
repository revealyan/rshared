using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace RShared.Redis;

/// <summary>
/// Dependency injection extensions.
/// </summary>
public static partial class RedisExtensions
{
	/// <summary>
	/// Adds Redis services: one multiplexer over the connection from the option.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configure">Redis options</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddRedis(this IServiceCollection services, Action<RedisOption> configure)
	{
		var option = BuildOption(configure);
		return services.AddRedisCore(option);
	}

	/// <summary>
	/// Adds Redis services bound to a configuration section ("redis" by default).
	/// Code-only settings (<c>Connection</c>, <c>ConfigureConnection</c>, <c>JsonSerializerOptions</c>)
	/// are not bindable and stay in code.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="section">Configuration section</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddRedis(this IServiceCollection services, IConfigurationSection section)
	{
		var option = new RedisOption();
		section.Bind(option);
		return services.AddRedisCore(BuildValidated(option));
	}

	/// <summary>
	/// Adds Redis services to a web application builder.
	/// </summary>
	public static WebApplicationBuilder AddRedis(this WebApplicationBuilder builder, Action<RedisOption> configure)
	{
		AddRedis(builder.Services, configure);
		return builder;
	}

	/// <summary>
	/// Adds Redis services bound to a configuration section to a web application builder.
	/// </summary>
	public static WebApplicationBuilder AddRedis(this WebApplicationBuilder builder, IConfigurationSection section)
	{
		AddRedis(builder.Services, section);
		return builder;
	}

	private static IServiceCollection AddRedisCore(this IServiceCollection services, RedisOption option)
	{
		services.TryAddSingleton(option);
		services.TryAddSingleton<IRedisConnection>(sp => new RedisConnection(sp.GetRequiredService<RedisOption>()));
		services.TryAddSingleton<IRedisCache, RedisCache>();
		services.TryAddSingleton<IRedisLock, RedisLock>();
		services.TryAddSingleton<IRedisRateLimiter, RedisRateLimiter>();
		services.TryAddSingleton<IRedisPublisher, RedisPublisher>();
		services.TryAddSingleton(new RedisSubscriptionRegistry());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RedisSubscriberService>());
		services.TryAddSingleton(new RedisStreamHandlerRegistry());
		services.TryAddSingleton<IRedisStreamPublisher, RedisStreamPublisher>();
		services.TryAddSingleton<IRedisNative, RedisNative>();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RedisStreamConsumerService>());
		return services;
	}

	/// <summary>
	/// Subscribe a durable stream handler: the message type is the stream, consumption
	/// is at-least-once within its consumer group. Several handlers of one type are invoked
	/// in registration order; settings come from the first registration.
	/// </summary>
	/// <typeparam name="TMessage">Message type; its name is the stream</typeparam>
	/// <param name="services">Service collection</param>
	/// <param name="handler">Message handler; must be idempotent</param>
	/// <param name="configure">Consumer group settings</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddRedisStreamHandler<TMessage>(this IServiceCollection services,
		Func<TMessage, Task> handler, Action<RedisStreamOption>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(handler);
		var settings = new RedisStreamOption();
		configure?.Invoke(settings);
		GetStreamHandlerRegistry(services).Add(handler, settings);
		return services;
	}

	/// <summary>
	/// Subscribe a durable stream handler with a synchronous callback.
	/// </summary>
	public static IServiceCollection AddRedisStreamHandler<TMessage>(this IServiceCollection services,
		Action<TMessage> handler, Action<RedisStreamOption>? configure = null)
	{
		return services.AddRedisStreamHandler<TMessage>(
			message =>
			{
				handler(message);
				return Task.CompletedTask;
			},
			configure);
	}

	private static RedisStreamHandlerRegistry GetStreamHandlerRegistry(IServiceCollection services)
	{
		var registry = services.FirstOrDefault(d =>
				d.ServiceType == typeof(RedisStreamHandlerRegistry))
			?.ImplementationInstance as RedisStreamHandlerRegistry;

		if (registry is null)
		{
			registry = new RedisStreamHandlerRegistry();
			services.TryAddSingleton(registry);
		}

		return registry;
	}

	/// <summary>
	/// Subscribe a handler to the channel of the message type: {ChannelPrefix}{TMessage name}.
	/// Several handlers of one type are invoked in registration order.
	/// </summary>
	/// <typeparam name="TMessage">Message type; its name is the channel</typeparam>
	/// <param name="services">Service collection</param>
	/// <param name="handler">Message handler</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddRedisSubscription<TMessage>(this IServiceCollection services, Action<TMessage> handler)
	{
		return services.AddRedisSubscription<TMessage>(message =>
		{
			handler(message);
			return Task.CompletedTask;
		});
	}

	/// <summary>
	/// Subscribe an async handler to the channel of the message type.
	/// </summary>
	public static IServiceCollection AddRedisSubscription<TMessage>(this IServiceCollection services, Func<TMessage, Task> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		GetSubscriptionRegistry(services).Add(handler);
		return services;
	}

	private static RedisSubscriptionRegistry GetSubscriptionRegistry(IServiceCollection services)
	{
		// Stryker disable LinqMethod : AddRedisCore уже зарегистрировал реестр — First==FirstOrDefault
		var registry = services.FirstOrDefault(d =>
				d.ServiceType == typeof(RedisSubscriptionRegistry))
			?.ImplementationInstance as RedisSubscriptionRegistry;
		// Stryker restore LinqMethod

		if (registry is null)
		{
			registry = new RedisSubscriptionRegistry();
			services.TryAddSingleton(registry);
		}

		return registry;
	}

	private static RedisOption BuildOption(Action<RedisOption> configure)
	{
		var option = new RedisOption();
		configure(option);
		return BuildValidated(option);
	}

	private static RedisOption BuildValidated(RedisOption option)
	{
		if (option.Connection is null && string.IsNullOrWhiteSpace(option.ConnectionString))
		{
			throw new ArgumentException("ConnectionString or Connection is required", nameof(option));
		}

		if (option.Connection is not null && !string.IsNullOrWhiteSpace(option.ConnectionString))
		{
			throw new ArgumentException("ConnectionString and Connection are mutually exclusive", nameof(option));
		}

		return option;
	}
}
