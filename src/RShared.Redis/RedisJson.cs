using System.Text.Json;

namespace RShared.Redis;

/// <summary>
/// JSON serialization for typed cache entries and messages.
/// </summary>
internal static class RedisJson
{
	public static byte[] Serialize<T>(T value, JsonSerializerOptions? options)
	{
		return JsonSerializer.SerializeToUtf8Bytes(value, options);
	}

	public static T Deserialize<T>(byte[] payload)
	{
		return JsonSerializer.Deserialize<T>(payload) ?? throw new JsonException("Payload is null");
	}
}
