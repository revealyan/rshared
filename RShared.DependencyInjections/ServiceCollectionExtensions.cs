using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace RShared.DependencyInjections;

public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddRegisties(IEnumerable<Assembly>? assemblies = null)
		{
			var registies = (assemblies ?? AppDomain.CurrentDomain.GetAssemblies())
				.SelectMany(a => a.GetTypes())
				.Where(t => t.IsAssignableTo(typeof(IRegistry)))
				.Where(t => !t.IsAbstract)
				.Where(t => !t.IsInterface)
				.Distinct()
				.Select(Activator.CreateInstance)
				.Cast<IRegistry>()
				.ToArray();

			foreach (var registry in registies)
			{
				registry.RegisterServices(services);
			}

			return services;
		}
	}
}
