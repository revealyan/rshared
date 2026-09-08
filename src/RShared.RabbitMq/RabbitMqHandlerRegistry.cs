namespace RShared.RabbitMq;

/// <summary>
/// Accumulates consumer endpoints; consumed by the hosted service at start
/// </summary>
internal sealed class RabbitMqHandlerRegistry
{
	private readonly List<RabbitMqEndpoint> _endpoints = [];

	public IReadOnlyList<RabbitMqEndpoint> Endpoints => _endpoints;

	public void Add(RabbitMqEndpoint endpoint)
	{
		if (_endpoints.Any(e => e.Queue == endpoint.Queue))
		{
			throw new InvalidOperationException($"Handler for queue \"{endpoint.Queue}\" is already registered");
		}

		_endpoints.Add(endpoint);
	}
}
