using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace RShared.RabbitMq;

/// <summary>
/// Registration of RabbitMq: typed handlers, publisher and hosted consumers
/// </summary>
public static class RabbitMqExtensions
{
	/// <summary>
	/// Register RabbitMq infrastructure: options, connection factory, publisher and the
	/// consumer hosted service. Queues are bound to handlers with
	/// <see cref="AddRabbitMqHandler{THandler,TMessage}"/> in the composition root.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configure">RabbitMq options</param>
	public static IServiceCollection AddRabbitMq(this IServiceCollection services, Action<RabbitMqOption> configure)
	{
		var option = new RabbitMqOption();

		configure(option);

		if (string.IsNullOrWhiteSpace(option.ConnectionString))
		{
			throw new ArgumentException("ConnectionString is required", nameof(configure));
		}

		// реестр нужен hosted-сервису даже без единого хендлера
		GetOrCreateRegistry(services);

		services.TryAddSingleton(option);
		services.TryAddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
		services.TryAddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RabbitMqConsumerService>());

		return services;
	}

	/// <summary>
	/// Bind a handler to its queue with topology. Queue names live here, in the composition
	/// root (infra or api layer) — the handler class stays free of infrastructure details.
	/// </summary>
	/// <typeparam name="THandler">Handler implementation</typeparam>
	/// <typeparam name="TMessage">Message type the body is deserialized to</typeparam>
	/// <param name="services">Service collection</param>
	/// <param name="queueName">Queue name, unique within the application</param>
	/// <param name="configureTopology">Queue topology overrides</param>
	public static IServiceCollection AddRabbitMqHandler<THandler, TMessage>(this IServiceCollection services,
		string queueName, Action<RabbitMqTopology>? configureTopology = null)
		where THandler : class, IRabbitMqHandler<TMessage>
	{
		var topology = new RabbitMqTopology();

		configureTopology?.Invoke(topology);

		GetOrCreateRegistry(services).Add(BuildEndpoint(queueName, topology, typeof(THandler), typeof(TMessage)));
		services.TryAddScoped<THandler>();

		return services;
	}

	private static RabbitMqHandlerRegistry GetOrCreateRegistry(IServiceCollection services)
	{
		var existing = services
			.Select(descriptor => descriptor.ImplementationInstance)
			.OfType<RabbitMqHandlerRegistry>()
			.FirstOrDefault();

		if (existing is not null)
		{
			return existing;
		}

		var registry = new RabbitMqHandlerRegistry();

		services.TryAddSingleton(registry);

		return registry;
	}

	private static RabbitMqEndpoint BuildEndpoint(string queueName, RabbitMqTopology topology,
		Type handlerType, Type messageType)
	{
		if (string.IsNullOrWhiteSpace(queueName))
		{
			throw new ArgumentException("Queue name is required", nameof(queueName));
		}

		var deadLetterQueue = topology.DeadLetterQueue is null
			? $"{queueName}.dlq"
			: topology.DeadLetterQueue.Length == 0 ? null : topology.DeadLetterQueue;

		return new RabbitMqEndpoint(
			queueName,
			topology.Exchange ?? string.Empty,
			topology.RoutingKey ?? queueName,
			topology.MaxRetryCount,
			deadLetterQueue,
			topology.Durable,
			handlerType,
			messageType);
	}
}
