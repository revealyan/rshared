using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RShared.Orm.EntityFrameworkCore;

public static class EntityFrameworkCoreExtensions
{
	public static IServiceCollection AddEntityRepositoryFactory<TDbContext>(this IServiceCollection services)
		where TDbContext : DbContext
	{
		services.AddScoped<IEntityRepositoryFactory, EntityRepositoryFactory<TDbContext>>(sp =>
		{
			var factory = sp.GetService<IDbContextFactory<TDbContext>>();
			if (factory is not null)
			{
				return new EntityRepositoryFactory<TDbContext>(factory, sp);
			}

			var context = sp.GetService<TDbContext>();
			if (context is not null)
			{
				return new EntityRepositoryFactory<TDbContext>(context, sp);
			}

			throw new NotSupportedException($"Not found {typeof(IDbContextFactory<TDbContext>)} or {typeof(TDbContext)}");
		});

		return services;
	}
}
