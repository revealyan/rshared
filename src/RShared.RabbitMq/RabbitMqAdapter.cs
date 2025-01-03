using Microsoft.Extensions.DependencyInjection;

namespace RShared.RabbitMq;

internal sealed class RabbitMqAdapter
	: IRabbitMqConsumerAdapter, IRabbitMqPublisherAdapter
{
	private readonly List<RabbitMqConfiguration> _configurations;
	private readonly List<RabbitMqConnectionAdapter> _connections;
	private readonly List<IRabbitMqMessageSerializer> _serializers;
	private readonly IDefaultRabbitMqMessageSerializer? _defaultSerializer;
	private readonly Dictionary<string, ProcessorDelegate> _processors;
	private readonly Dictionary<string, RabbitMqChannelAdapter> _channels;
	private readonly IServiceProvider _serviceProvider;

	public RabbitMqAdapter(IEnumerable<RabbitMqConfiguration> configurations, IServiceProvider serviceProvider,
		IEnumerable<IRabbitMqMessageSerializer> serializers, IDefaultRabbitMqMessageSerializer? defaultSerializer)
	{
		_configurations = (configurations ?? throw new ArgumentNullException(nameof(configurations))).ToList();
		_serializers = (serializers ?? throw new ArgumentNullException(nameof(serializers))).ToList();
		_defaultSerializer = defaultSerializer;
		_connections = new();
		_processors = new();
		_channels = new();
		_serviceProvider = serviceProvider;
	}



	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		VerifyProcessors();

		foreach (var configuration in _configurations)
		{
			var connection = new RabbitMqConnectionAdapter(configuration);

			await connection.OpenConnectionAsync(cancellationToken);

			foreach (var queueConfiguration in configuration.Queues)
			{
				_processors.TryGetValue(queueConfiguration.Id, out var processor);
				var channel = await connection.CreateChannelAsync(queueConfiguration, processor, cancellationToken);

				if (!_channels.TryAdd(queueConfiguration.Id, channel))
				{
					throw new InvalidOperationException($"Channel already exists for queue {queueConfiguration.Id}");
				}
			}

			_connections.Add(connection);
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		foreach (var connection in _connections)
		{
			await connection.CloseConnectionAsync(cancellationToken);
		}

		_channels.Clear();
		_connections.Clear();
	}

	public async Task PublishAsync<T>(string queueId, T data, CancellationToken cancellationToken = default)
	{
		if (!_channels.TryGetValue(queueId, out var channel))
		{
			throw new InvalidOperationException($"Channel not found for queue {queueId}");
		}

		var message = await SerializeAsync(data, cancellationToken);

		await channel.SendAsync(message, cancellationToken);
	}

	public Task PublishAsync(string queueId, RabbitMqMessage message, CancellationToken cancellationToken = default)
	{
		if (!_channels.TryGetValue(queueId, out var channel))
		{
			throw new InvalidOperationException($"Channel not found for queue {queueId}");
		}

		return channel.SendAsync(message, cancellationToken);
	}



	private void VerifyProcessors()
	{
		using var scope = _serviceProvider.CreateScope();

		var processors = scope.ServiceProvider.GetServices<IRabbitMqMessageProcessor>() ?? [];

		foreach (var processor in processors)
		{
			if (!_processors.TryAdd(processor.QueueId, CreateProcessorDelegate(processor.QueueId)))
			{
				throw new InvalidOperationException($"Processor already exists for queue {processor.QueueId}");
			}
		}
	}

	private ProcessorDelegate CreateProcessorDelegate(string queueId)
	{
		async Task<bool> ProcessorDelegateInternal(RabbitMqMessage message, CancellationToken cancellation)
		{
			using var scope = _serviceProvider.CreateScope();

			var processors = scope.ServiceProvider.GetServices<IRabbitMqMessageProcessor>() ?? [];

			var processor = processors.Single(p => p.QueueId == queueId);

			return await processor.ProcessAsync(message, cancellation);
		}

		return ProcessorDelegateInternal;
	}

	private Task<RabbitMqMessage> SerializeAsync<T>(T data, CancellationToken cancellationToken)
	{
		var serializer = _serializers.OfType<IRabbitMqMessageSerializer<T>>().FirstOrDefault();

		if (serializer is not null)
		{
			return serializer.SerializeAsync(data, cancellationToken);
		}

		if (_defaultSerializer is not null)
		{
			return _defaultSerializer.SerializeAsync(data, cancellationToken);
		}

		throw new InvalidOperationException($"Can not find serilazer for type {typeof(T)}");
	}
}
