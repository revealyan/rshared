using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace RShared.RabbitMq;

internal sealed class RabbitMqChannelAdapter
{
	internal static async Task<RabbitMqChannelAdapter> CreateAsync(IChannel channel, IRabbitMqMessageProcessor? processor,
		RabbitMqQueueConfiguration configuration, CancellationToken cancellationToken = default)
	{
		var adapter = new RabbitMqChannelAdapter(channel, processor, configuration);

		await adapter.InitAsync(cancellationToken);

		return adapter;
	}

	/// <summary>
	/// Channel of RabbitMq
	/// </summary>
	private readonly IChannel _channel;

	/// <summary>
	/// Message processor of queue
	/// </summary>
	private readonly IRabbitMqMessageProcessor? _messageProcessor;

	/// <summary>
	/// Queue configuraiton
	/// </summary>
	private readonly RabbitMqQueueConfiguration _configuration;

	/// <summary>
	/// Initialized instance of rabbitmq channel adapter
	/// </summary>
	/// <param name="channel"></param>
	/// <param name="messageProcessor"></param>
	private RabbitMqChannelAdapter(IChannel channel, IRabbitMqMessageProcessor? messageProcessor, RabbitMqQueueConfiguration configuration)
	{
		_channel = channel;
		_messageProcessor = messageProcessor;
		_configuration = configuration;
	}

	public async Task SendAsync(RabbitMqMessage message, CancellationToken cancellationToken = default)
	{
		await _channel.BasicPublishAsync(_configuration.ExchangeName, _configuration.QueueName, false,
			message.GetProperties(), message.GetBody(), cancellationToken);
	}

	private async Task InitAsync(CancellationToken cancellationToken = default)
	{
		if (_messageProcessor is not null)
		{
			var consumer = new AsyncEventingBasicConsumer(_channel);

			consumer.ReceivedAsync += (_, args) =>
			{
				var message = new RabbitMqMessage
				{
					MessageId = args.BasicProperties?.MessageId ?? string.Empty,
					CorrelationId = args.BasicProperties?.CorrelationId ?? string.Empty,
					Data = Encoding.UTF8.GetString(args.Body.ToArray()),
					Properties = new Dictionary<string, object?>(0),
				};

				return _messageProcessor.ProcessAsync(message, args.CancellationToken);
			};


			await _channel.BasicConsumeAsync(_configuration.QueueName, true, consumer, cancellationToken).ConfigureAwait(false);
		}
	}
}
