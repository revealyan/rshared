using System.Text.Json;

namespace RShared.RabbitMq;

internal static class RabbitMqJson
{
	public static byte[] Serialize<T>(T value, JsonSerializerOptions? options)
	{
		return JsonSerializer.SerializeToUtf8Bytes(value, options);
	}

	/// <summary>
	/// Deserialize a message body; null payload is treated as a poison message
	/// </summary>
	public static object Deserialize(Type type, ReadOnlyMemory<byte> body, JsonSerializerOptions? options)
	{
		return JsonSerializer.Deserialize(body.Span, type, options)
			?? throw new JsonException($"Message body deserialized to null for {type.Name}");
	}
}
