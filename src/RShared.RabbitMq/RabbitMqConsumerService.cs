using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace RShared.RabbitMq;

/// <summary>
/// Hosted service that consumes all registered endpoints: declares topology
/// (queue, dead-letter queue, binding), sets prefetch and dispatches deliveries
/// to scoped handlers with the failure policy.
/// </summary>
internal sealed class RabbitMqConsumerService
	: IHostedService
{
	private readonly RabbitMqHandlerRegistry _registry;
	private readonly RabbitMqOption _option;
	private readonly IRabbitMqConnectionFactory _connectionFactory;
	private readonly IServiceProvider _provider;
	private readonly ILogger<RabbitMqConsumerService> _logger;
	private readonly RabbitMqDeliveryProcessor _processor = new();
	private readonly List<IChannel> _channels = [];

	private IConnection? _connection;

	public RabbitMqConsumerService(RabbitMqHandlerRegistry registry, RabbitMqOption option,
		IRabbitMqConnectionFactory connectionFactory, IServiceProvider provider,
		ILogger<RabbitMqConsumerService>? logger = null)
	{
		_registry = registry;
		_option = option;
		_connectionFactory = connectionFactory;
		_provider = provider;
		_logger = logger ?? NullLogger<RabbitMqConsumerService>.Instance;
	}

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (_registry.Endpoints.Count == 0)
		{
			_logger.LogInformation("No RabbitMq handlers registered — consumers not started");
			return;
		}

		_logger.LogInformation("Starting RabbitMq consumers for {count} queue(s)", _registry.Endpoints.Count);

		_connection = await _connectionFactory.CreateAsync(_option, cancellationToken).ConfigureAwait(false);

		foreach (var endpoint in _registry.Endpoints)
		{
			var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

			await DeclareTopologyAsync(channel, endpoint, cancellationToken).ConfigureAwait(false);
			await channel.BasicQosAsync(0, _option.PrefetchCount, false, cancellationToken).ConfigureAwait(false);

			var consumer = new AsyncEventingBasicConsumer(channel);

			consumer.ReceivedAsync += (_, args) => DispatchAsync(endpoint, channel, args);

			await channel.BasicConsumeAsync(endpoint.Queue, false, consumer, cancellationToken).ConfigureAwait(false);

			_channels.Add(channel);
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		foreach (var channel in _channels)
		{
			// CloseAsync останавливает консюмеры и ждёт незавершённые доставки
			await channel.CloseAsync(cancellationToken).ConfigureAwait(false);
			await channel.DisposeAsync().ConfigureAwait(false);
		}

		_channels.Clear();

		if (_connection is not null)
		{
			await _connection.CloseAsync(cancellationToken).ConfigureAwait(false);
			await _connection.DisposeAsync().ConfigureAwait(false);
			_connection = null;
		}
	}

	private static async Task DeclareTopologyAsync(IChannel channel, RabbitMqEndpoint endpoint, CancellationToken cancellationToken)
	{
		var arguments = new Dictionary<string, object?>();

		if (endpoint.DeadLetterQueue is not null)
		{
			// недоставленные (nack без requeue) уходят через default exchange в dead-letter очередь
			arguments["x-dead-letter-exchange"] = string.Empty;
			arguments["x-dead-letter-routing-key"] = endpoint.DeadLetterQueue;

			await channel.QueueDeclareAsync(endpoint.DeadLetterQueue, endpoint.Durable, false, false,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		await channel.QueueDeclareAsync(endpoint.Queue, endpoint.Durable, false, false, arguments,
			cancellationToken: cancellationToken).ConfigureAwait(false);

		if (endpoint.Exchange.Length > 0)
		{
			await channel.ExchangeDeclareAsync(endpoint.Exchange, ExchangeType.Direct, endpoint.Durable,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			await channel.QueueBindAsync(endpoint.Queue, endpoint.Exchange, endpoint.RoutingKey,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Одна доставка: скоуп → десериализация → хендлер → ack/nack по failure policy.
	/// Битая сериализация — сразу в dead-letter, без ретраев.
	/// </summary>
	internal async Task DispatchAsync(RabbitMqEndpoint endpoint, IChannel channel, BasicDeliverEventArgs args)
	{
		using var scope = _provider.CreateScope();

		object message;

		try
		{
			message = RabbitMqJson.Deserialize(endpoint.MessageType, args.Body, _option.JsonSerializerOptions);
		}
		catch (JsonException exception)
		{
			_logger.LogError(exception, "Poison message in queue {queue}: body does not deserialize to {type}",
				endpoint.Queue, endpoint.MessageType.Name);

			await channel.BasicNackAsync(args.DeliveryTag, false, false, args.CancellationToken).ConfigureAwait(false);
			return;
		}

		var messageId = args.BasicProperties?.MessageId ?? string.Empty;

		try
		{
			await _processor.ProcessAsync(
				messageId.Length == 0 ? string.Empty : $"{endpoint.Queue}:{messageId}",
				endpoint.MaxRetryCount,
				cancellationToken => endpoint.InvokeAsync(scope.ServiceProvider, message, cancellationToken),
				cancellationToken => channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken).AsTask(),
				(requeue, cancellationToken) => channel.BasicNackAsync(args.DeliveryTag, false, requeue, cancellationToken).AsTask(),
				args.CancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// шатдаун посреди обработки: сообщение не подтверждено и вернётся после старта
			_logger.LogInformation("Delivery cancelled during shutdown, queue {queue}", endpoint.Queue);
		}
	}
}
