using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace RShared.RabbitMq;

/// <summary>
/// Publisher over its own connection: a single channel in confirm mode,
/// publishes serialized one at a time, channel is recreated after failures.
/// </summary>
internal sealed class RabbitMqPublisher
	: IRabbitMqPublisher
{
	private readonly RabbitMqOption _option;
	private readonly IRabbitMqConnectionFactory _connectionFactory;
	private readonly ILogger<RabbitMqPublisher> _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private IConnection? _connection;
	private IChannel? _channel;

	public RabbitMqPublisher(RabbitMqOption option, IRabbitMqConnectionFactory connectionFactory,
		ILogger<RabbitMqPublisher>? logger = null)
	{
		_option = option;
		_connectionFactory = connectionFactory;
		_logger = logger ?? NullLogger<RabbitMqPublisher>.Instance;
	}

	public async Task PublishAsync<TMessage>(string queueName, TMessage message, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);
			var body = RabbitMqJson.Serialize(message, _option.JsonSerializerOptions);
			var properties = new BasicProperties
			{
				MessageId = Guid.NewGuid().ToString("N"),
				CorrelationId = Activity.Current?.Id,
				Type = typeof(TMessage).Name,
				ContentType = "application/json",
				DeliveryMode = _option.PersistentMessages ? DeliveryModes.Persistent : DeliveryModes.Transient,
			};

			await channel.BasicPublishAsync(string.Empty, queueName, false, properties, body, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
	{
		if (_channel is { IsOpen: true })
		{
			return _channel;
		}

		if (_connection is not { IsOpen: true })
		{
			_logger.LogDebug("Opening publisher connection");
			_connection = await _connectionFactory.CreateAsync(_option, cancellationToken).ConfigureAwait(false);
		}

		_logger.LogDebug("Opening publisher channel");

		// confirms включаются при создании канала: с трекингом BasicPublishAsync
		// завершается только после подтверждения брокера
		var channelOptions = new CreateChannelOptions(
			publisherConfirmationsEnabled: _option.PublisherConfirms,
			publisherConfirmationTrackingEnabled: _option.PublisherConfirms);

		_channel = await _connection.CreateChannelAsync(channelOptions, cancellationToken).ConfigureAwait(false);

		return _channel;
	}
}
