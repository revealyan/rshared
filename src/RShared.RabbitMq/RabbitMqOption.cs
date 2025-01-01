using System.Reflection;
using System.Text.Json;

namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq option
/// </summary>
public class RabbitMqOption
{
	/// <summary>
	/// If <c>True</c> then adding all <see cref="IRabbitMqMessageProcessor"/> implementations to dependency injection from 
	/// </summary>
	public bool AddAllProcessors { get; set; } = true;

	/// <summary>
	/// Assemblies for searching <see cref="IRabbitMqMessageProcessor"/> implementations. If it's <c>null</c>, then using <see cref="AppDomain.CurrentDomain"/>
	/// </summary>
	public IEnumerable<Assembly>? Assemblies { get; set; }

	/// <summary>
	/// Use default serializer if not found specified
	/// </summary>
	public bool UseDefaultSerializer { get; set; } = true;

	/// <summary>
	/// Json serializer options
	/// </summary>
	public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
