using System.Reflection;

namespace RShared.Mediator;

/// <summary>
/// Mediator dependency injection configuration options
/// </summary>
public class MediatorOption
{
	/// <summary>
	/// If it's <c>True</c>, then added all <see cref="IMessageHandler"/> implementation to dependency injection from <see cref="Assemblies"/>.
	/// If it's <c>False</c>, then do nothing
	/// <c>True</c> by default.
	/// </summary>
	public bool AddHandlers { get; set; } = true;

	/// <summary>
	/// Assemblies for searching <see cref="IMessageHandler"/> handlers to adding to dependecy injection.
	/// If it's <c>null</c> then used <see cref="AppDomain.CurrentDomain"/> for searching.
	/// <c>Null</c> by default.
	/// </summary>
	public IEnumerable<Assembly>? Assemblies { get; set; }
}
