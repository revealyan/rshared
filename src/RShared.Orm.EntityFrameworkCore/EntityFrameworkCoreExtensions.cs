using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RShared.Orm;

namespace RShared.Orm.EntityFrameworkCore;

/// <summary>
/// Registration of entity repositories over EF Core
/// </summary>
public static class EntityFrameworkCoreExtensions
{
	/// <summary>
	/// Register entity repository factory over given contexts.
	/// Entity ownership is declared by the context model itself: DbSet's and OnModelCreating —
	/// what a context configures is what it owns.
	/// Contexts are resolved through IDbContextFactory&lt;T&gt; when registered, otherwise from the scope.
	/// Ownership map is warmed up at host start by a hosted service; without a host — on first use.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="contextTypes">DbContext types to serve</param>
	/// <exception cref="ArgumentException">Throws when types are empty or not a concrete DbContext</exception>
	public static IServiceCollection AddEntityRepositories(this IServiceCollection services, params Type[] contextTypes)
	{
		var registry = new EntityRepositoryRegistry(contextTypes);

		services.TryAddSingleton(registry);
		services.TryAddScoped<IEntityRepositoryFactory, EntityRepositoryFactory>();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EntityRepositoryRegistryWarmUp>());

		return services;
	}
}
