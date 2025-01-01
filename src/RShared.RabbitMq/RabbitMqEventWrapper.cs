namespace RShared.RabbitMq;

public class RabbitMqEventWrapper
{
	public string Type { get; set; } = string.Empty;
	public object? Data { get; set; }
}
