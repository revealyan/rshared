using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RShared.Orm;
using Xunit;

namespace RShared.Orm.EntityFrameworkCore.Tests;

public class RepositoryTests
{
	[Fact]
	public async Task Create_routes_entities_to_owning_contexts()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<Order>().InsertAsync(new Order { Id = 1, Amount = 100 });
			await factory.Create<Payment>().InsertAsync(new Payment { Id = 7, OrderId = 1 });
			await unitOfWork.CommitAsync();
		}

		using (var checkScope = fixture.Provider.CreateScope())
		{
			var catalog = checkScope.ServiceProvider.GetRequiredService<CatalogContext>();
			var billing = checkScope.ServiceProvider.GetRequiredService<BillingContext>();

			Assert.Equal(100m, catalog.Orders.Single().Amount);
			Assert.Equal(1, billing.Payments.Single().OrderId);
		}
	}

	[Fact]
	public void Create_throws_when_entity_owned_by_two_contexts()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();

		var services = new ServiceCollection()
			.AddEntityRepositories(typeof(CatalogContext), typeof(ConflictContext));
		services.AddDbContext<CatalogContext>(options => options.UseSqlite(connection));
		services.AddDbContext<ConflictContext>(options => options.UseSqlite(connection));

		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();

		var exception = Assert.Throws<InvalidOperationException>(
			() => scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>().Create<Order>());

		Assert.Contains("CatalogContext", exception.Message);
		Assert.Contains("ConflictContext", exception.Message);
	}

	[Fact]
	public void Create_throws_when_entity_has_no_owner()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		var exception = Assert.Throws<InvalidOperationException>(
			() => factory.Create<UnknownEntity>());

		Assert.Contains("No registered context owns entity", exception.Message);
	}

	[Fact]
	public void Create_throws_when_context_is_not_registered_in_di()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();

		var services = new ServiceCollection()
			.AddEntityRepositories(typeof(CatalogContext), typeof(GhostContext));
		services.AddDbContext<CatalogContext>(options => options.UseSqlite(connection));

		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();

		// Payment принадлежит GhostContext, которого нет в DI
		var exception = Assert.Throws<NotSupportedException>(
			() => scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>().Create<Payment>());

		Assert.Contains("neither", exception.Message);
	}

	[Fact]
	public async Task Factory_created_contexts_are_used_when_registered()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();

		var services = new ServiceCollection()
			.AddEntityRepositories(typeof(CatalogContext));
		services.AddDbContextFactory<CatalogContext>(options => options.UseSqlite(connection));

		using var provider = services.BuildServiceProvider();

		provider.GetRequiredService<IDbContextFactory<CatalogContext>>()
			.CreateDbContext()
			.Database.EnsureCreated();

		DbContext ownedContext;

		using (var scope = provider.CreateScope())
		{
			var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

			// контекст резолвится один раз на скоуп и переиспользуется
			ownedContext = ((IContextBoundRepository)factory.Create<Order>()).Context;

			Assert.Same(ownedContext, ((IContextBoundRepository)factory.Create<Order>()).Context);

			await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
			{
				await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
				await unitOfWork.CommitAsync();
			}
		}

		// контексты, созданные через IDbContextFactory, диспозит сама фабрика репозиториев
		Assert.ThrowsAny<ObjectDisposedException>(() => ownedContext.Set<Order>().ToList());

		using var check = provider.GetRequiredService<IDbContextFactory<CatalogContext>>().CreateDbContext();

		Assert.Single(check.Orders.ToList());
	}

	[Fact]
	public async Task Query_filters_entities_through_iqueryable()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<Order>().InsertAsync(new Order { Id = 1, Amount = 100 });
			await factory.Create<Order>().InsertAsync(new Order { Id = 2, Amount = 40 });
			await unitOfWork.CommitAsync();
		}

		var big = factory.Create<Order>().Query()
			.Where(order => order.Amount > 50)
			.ToList();

		Assert.Single(big);
		Assert.Equal(1, big[0].Id);
	}

	[Fact]
	public void Query_throws_on_foreign_repository()
	{
		var repository = new ForeignRepository();

		var exception = Assert.Throws<NotSupportedException>(() => repository.Query());

		Assert.Contains("Query is available for EF repositories only", exception.Message);
	}

	[Fact]
	public async Task DeleteAsync_removes_entity()
	{
		using var fixture = OrmFixture.Create();

		using (var scope = fixture.Provider.CreateScope())
		{
			var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

			await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
			{
				await factory.Create<Order>().InsertAsync(new Order { Id = 1, Amount = 100 });
				await unitOfWork.CommitAsync();
			}
		}

		using (var scope = fixture.Provider.CreateScope())
		{
			var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

			// удаление — первая мутация своего unit of work, контекст ещё не в транзакции
			await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
			{
				await factory.Create<Order>().DeleteAsync(new Order { Id = 1 });
				await unitOfWork.CommitAsync();
			}
		}

		Assert.Null(await ReadOrderAsync(fixture, 1));
	}

	[Fact]
	public async Task AddAsync_inserts_new_entity()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<Order>().AddAsync(new Order { Id = 1, Amount = 100 });
			await unitOfWork.CommitAsync();
		}

		var order = await ReadOrderAsync(fixture, 1);

		Assert.NotNull(order);
		Assert.Equal(100m, order.Amount);
	}

	[Fact]
	public async Task AddAsync_updates_existing_entity()
	{
		using var fixture = OrmFixture.Create();

		using (var scope = fixture.Provider.CreateScope())
		{
			var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

			await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
			{
				await factory.Create<Order>().InsertAsync(new Order { Id = 1, Amount = 100 });
				await unitOfWork.CommitAsync();
			}
		}

		// свежий скоуп: трекер пуст, ContainsAsync смотрит в базу
		using (var scope = fixture.Provider.CreateScope())
		{
			var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

			await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
			{
				await factory.Create<Order>().AddAsync(new Order { Id = 1, Amount = 250 });
				await unitOfWork.CommitAsync();
			}
		}

		var order = await ReadOrderAsync(fixture, 1);

		Assert.NotNull(order);
		Assert.Equal(250m, order.Amount);
	}

	private static async Task<Order?> ReadOrderAsync(OrmFixture fixture, int id)
	{
		using var scope = fixture.Provider.CreateScope();

		return await scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>()
			.Create<Order>()
			.GetAsync(id);
	}

	/// <summary>
	/// Репозиторий не от EF — Query для него недоступен
	/// </summary>
	private sealed class ForeignRepository
		: IEntityRepository<Order>
	{
		public Task<Order> InsertAsync(Order entity, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<Order?> GetAsync<TKey>(TKey key, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<Order> AddAsync(Order entity, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<Order?> DeleteAsync(Order entity, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();
	}
}
