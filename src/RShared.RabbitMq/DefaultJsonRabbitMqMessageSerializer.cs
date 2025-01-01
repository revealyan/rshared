using System.Text.Json;

namespace RShared.RabbitMq;

/// <summary>
/// Default json message Serializer
/// </summary>
public class DefaultJsonRabbitMqMessageSerializer
	: IDefaultRabbitMqMessageSerializer
{
	private readonly JsonSerializerOptions _options;

	public DefaultJsonRabbitMqMessageSerializer(JsonSerializerOptions? options)
	{
		_options = options ?? JsonSerializerOptions.Default;
	}

	public Task<T?> DeserializeAsync<T>(RabbitMqMessage message, CancellationToken cancellationToken)
	{
		return Task.FromResult(JsonSerializer.Deserialize<T>(message?.Data ?? string.Empty, _options));
	}

	public Task<RabbitMqMessage> SerializeAsync<T>(T data, CancellationToken cancellationToken)
	{
		var message = new RabbitMqMessage
		{
			Data = JsonSerializer.Serialize(data, _options),
		};

		return Task.FromResult(message);
	}
}