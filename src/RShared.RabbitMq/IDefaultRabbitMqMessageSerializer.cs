namespace RShared.RabbitMq;

/// <summary>
/// Default message Serializer
/// </summary>
public interface IDefaultRabbitMqMessageSerializer
{
	public Task<T?> DeserializeAsync<T>(RabbitMqMessage message, CancellationToken cancellationToken);
	public Task<RabbitMqMessage> SerializeAsync<T>(T message, CancellationToken cancellationToken);
}
