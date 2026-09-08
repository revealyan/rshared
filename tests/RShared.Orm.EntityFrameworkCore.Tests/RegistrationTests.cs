using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RShared.Orm;
using Xunit;

namespace RShared.Orm.EntityFrameworkCore.Tests;

public class RegistrationTests
{
	[Fact]
	public void AddEntityRepositories_registers_factory_registry_and_warm_up()
	{
		var services = new ServiceCollection()
			.AddEntityRepositories(typeof(CatalogContext));

		Assert.Contains(services, sd => sd.ServiceType == typeof(IEntityRepositoryFactory));
		Assert.Contains(services, sd => sd.ServiceType == typeof(EntityRepositoryRegistry));
		Assert.Contains(services, sd => sd.ServiceType == typeof(IHostedService)
			&& sd.ImplementationType == typeof(EntityRepositoryRegistryWarmUp));
	}

	[Fact]
	public async Task Warm_up_fails_fast_on_ownership_conflict()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();

		var services = new ServiceCollection()
			.AddEntityRepositories(typeof(CatalogContext), typeof(ConflictContext));
		services.AddDbContext<CatalogContext>(options => options.UseSqlite(connection));
		services.AddDbContext<ConflictContext>(options => options.UseSqlite(connection));

		using var provider = services.BuildServiceProvider();

		// StartAsync хоста должен уронить приложение на старте, а не в первом реквесте
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => provider.GetRequiredService<IHostedService>().StartAsync(CancellationToken.None));

		Assert.Contains("CatalogContext", exception.Message);
		Assert.Contains("ConflictContext", exception.Message);
	}

	[Fact]
	public void AddEntityRepositories_requires_at_least_one_context()
	{
		var exception = Assert.Throws<ArgumentException>(
			() => new ServiceCollection().AddEntityRepositories());

		Assert.Contains("At least one DbContext type is required", exception.Message);
	}

	[Fact]
	public void AddEntityRepositories_throws_on_type_outside_DbContext_hierarchy()
	{
		var exception = Assert.Throws<ArgumentException>(
			() => new ServiceCollection().AddEntityRepositories(typeof(object)));

		Assert.Contains("is not a concrete DbContext", exception.Message);
	}

	[Fact]
	public void AddEntityRepositories_throws_on_abstract_context()
	{
		var exception = Assert.Throws<ArgumentException>(
			() => new ServiceCollection().AddEntityRepositories(typeof(AbstractContext)));

		Assert.Contains("is not a concrete DbContext", exception.Message);
	}
}
