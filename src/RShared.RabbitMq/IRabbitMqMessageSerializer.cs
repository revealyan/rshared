
namespace RShared.RabbitMq;

/// <summary>
/// Interface marker
/// </summary>
public interface IRabbitMqMessageSerializer
{

}

/// <summary>
/// Message serializer for some type
/// </summary>
public interface IRabbitMqMessageSerializer<T>
	: IRabbitMqMessageSerializer
{
	public Task<T?> DeserializeAsync(RabbitMqMessage message, CancellationToken cancellationToken);
	public Task<RabbitMqMessage> SerializeAsync(T message, CancellationToken cancellationToken);
}
