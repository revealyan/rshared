using RabbitMQ.Client;

namespace RShared.RabbitMq;

internal interface IRabbitMqConnectionFactory
{
	Task<IConnection> CreateAsync(RabbitMqOption option, CancellationToken cancellationToken);
}

internal sealed class RabbitMqConnectionFactory
	: IRabbitMqConnectionFactory
{
	// Stryker disable all : тонкая прокладка к брокеру — проверяется только с живым RabbitMQ
	public async Task<IConnection> CreateAsync(RabbitMqOption option, CancellationToken cancellationToken)
	{
		return await BuildFactory(option).CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Map option to the client factory without touching the network
	/// </summary>
	internal static ConnectionFactory BuildFactory(RabbitMqOption option)
	{
		return new ConnectionFactory
		{
			ClientProvidedName = option.ClientName,
			Uri = new Uri(option.ConnectionString),
		};
	}
}
