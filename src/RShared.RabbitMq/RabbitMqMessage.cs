using RabbitMQ.Client;
using System.Text;

namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq message wrapper
/// </summary>
public class RabbitMqMessage
{
	/// <summary>
	/// Message id
	/// </summary>
	public string MessageId { get; set; } = string.Empty;

	/// <summary>
	/// Correlation id
	/// </summary>
	public string CorrelationId { get; set; } = string.Empty;

	/// <summary>
	/// Properties of message
	/// </summary>
	public IReadOnlyDictionary<string, object?> Properties { get; set; } = new Dictionary<string, object?>();

	/// <summary>
	/// Message data
	/// </summary>
	public string? Data { get; set; }

	/// <summary>
	/// Getting body ready for using for RabbitMq.Client
	/// </summary>
	/// <returns>Readonly memory bytes</returns>
	public virtual ReadOnlyMemory<byte> GetBody()
	{
		return Data is null ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(Data));
	}

	/// <summary>
	/// Getting properties ready for using for RabbitMq.Client
	/// </summary>
	/// <returns>Basic properties</returns>
	public virtual BasicProperties GetProperties()
	{
		return new BasicProperties();
	}
}
