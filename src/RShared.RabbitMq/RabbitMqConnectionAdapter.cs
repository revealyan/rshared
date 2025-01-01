using RabbitMQ.Client;

namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq adapter
/// </summary>
internal sealed class RabbitMqConnectionAdapter
{
	/// <summary>
	/// Client name
	/// </summary>
	private readonly string _name;

	/// <summary>
	/// RabbitMq connection configuration
	/// </summary>
	private readonly RabbitMqConfiguration _configuration;

	/// <summary>
	/// RabbitMq connection factory
	/// </summary>
	private readonly ConnectionFactory _factory;

	/// <summary>
	/// Current connection to RabbitMq
	/// </summary>
	private IConnection? _connection;



	public RabbitMqConnectionAdapter(RabbitMqConfiguration configuration)
	{
		_configuration = configuration ?? throw new ArgumentNullException("Connection configuration");
		_name = configuration?.ClientName ?? throw new ArgumentNullException("Client provided name");

		_factory = CreateFactory();
	}



	public async Task OpenConnectionAsync(CancellationToken cancellationToken = default)
	{
		if (_connection is not null && _connection.IsOpen)
		{
			return;
		}

		_connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task CloseConnectionAsync(CancellationToken cancellationToken = default)
	{
		if (_connection is not null && _connection.IsOpen)
		{
			await _connection.CloseAsync(cancellationToken).ConfigureAwait(false);
		}

		_connection = null;
	}

	public async Task<RabbitMqChannelAdapter> CreateChannelAsync(RabbitMqQueueConfiguration queueConfiguration,
		ProcessorDelegate? processor, CancellationToken cancellationToken = default)
	{
		if (_connection is not null && _connection.IsOpen)
		{
			var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

			return await RabbitMqChannelAdapter.CreateAsync(channel, queueConfiguration, processor);
		}

		throw new InvalidOperationException("Connection closed");
	}



	private ConnectionFactory CreateFactory()
	{
		return new ConnectionFactory
		{
			ClientProvidedName = _name,
			Uri = new Uri(_configuration.ConnectionString),
		};
	}
}
