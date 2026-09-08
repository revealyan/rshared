using System.Data;
using Microsoft.Extensions.DependencyInjection;
using RShared.Orm;
using Xunit;

namespace RShared.Orm.EntityFrameworkCore.Tests;

public class UnitOfWorkTests
{
	[Fact]
	public async Task Transaction_opens_only_in_mutated_contexts()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
		var billing = scope.ServiceProvider.GetRequiredService<BillingContext>();

		await using (factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			Assert.Null(catalog.Database.CurrentTransaction);

			await factory.Create<Order>().InsertAsync(new Order { Id = 1 });

			Assert.NotNull(catalog.Database.CurrentTransaction);
			Assert.Null(billing.Database.CurrentTransaction);
		}

		Assert.Null(catalog.Database.CurrentTransaction);
	}

	[Fact]
	public async Task Commit_persists_changes()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<Order>().InsertAsync(new Order { Id = 1, Amount = 100 });
			await unitOfWork.CommitAsync();
		}

		var order = await ReadOrderAsync(fixture, 1);

		Assert.NotNull(order);
		Assert.Equal(100m, order.Amount);
	}

	[Fact]
	public async Task Dispose_without_commit_discards_changes()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		await using (factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
		}

		Assert.Null(await ReadOrderAsync(fixture, 1));
	}

	[Fact]
	public async Task Rollback_discards_changes()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
			await unitOfWork.RollbackAsync();
		}

		Assert.Null(await ReadOrderAsync(fixture, 1));
	}

	[Fact]
	public async Task Nested_commit_is_checkpoint_and_root_commits_everything()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		var root = factory.CreateUnitOfWork(IsolationLevel.Serializable);
		var inner = factory.CreateUnitOfWork(IsolationLevel.Serializable);

		await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
		await inner.CommitAsync();

		// после чекпоинта скоуп жив: мутации продолжают попадать в корневой коммит
		await factory.Create<Payment>().InsertAsync(new Payment { Id = 7, OrderId = 1 });
		await root.CommitAsync();

		inner.Dispose();
		root.Dispose();

		using var checkScope = fixture.Provider.CreateScope();

		Assert.NotNull(await checkScope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>()
			.Create<Order>().GetAsync(1));
		Assert.NotNull(await checkScope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>()
			.Create<Payment>().GetAsync(7));
	}

	[Fact]
	public async Task Rollback_after_nested_checkpoint_discards_everything()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		var root = factory.CreateUnitOfWork(IsolationLevel.Serializable);
		var inner = factory.CreateUnitOfWork(IsolationLevel.Serializable);

		await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
		await inner.CommitAsync();
		await root.RollbackAsync();

		inner.Dispose();
		root.Dispose();

		Assert.Null(await ReadOrderAsync(fixture, 1));
	}

	[Fact]
	public async Task Root_disposed_before_nested_rolls_back_and_throws()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		var root = factory.CreateUnitOfWork(IsolationLevel.Serializable);
		var inner = factory.CreateUnitOfWork(IsolationLevel.Serializable);

		await factory.Create<Order>().InsertAsync(new Order { Id = 1 });

		var exception = Assert.Throws<InvalidOperationException>(root.Dispose);

		Assert.Contains("Root unit of work is disposed", exception.Message);

		// транзакции откачены и отпущены сразу: коннект свободен для нового скоупа
		using (var readerScope = fixture.Provider.CreateScope())
		{
			var reader = readerScope.ServiceProvider.GetRequiredService<CatalogContext>();

			using (reader.Database.BeginTransaction())
			{
			}
		}

		// скоуп откачен — вложенному коммитить больше некуда
		await Assert.ThrowsAsync<InvalidOperationException>(() => inner.CommitAsync());

		inner.Dispose();

		Assert.Null(await ReadOrderAsync(fixture, 1));
	}

	[Fact]
	public async Task Mutations_after_commit_throw()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		await using var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable);

		await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
		await unitOfWork.CommitAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => factory.Create<Order>().InsertAsync(new Order { Id = 2 }));
	}

	[Fact]
	public async Task Flush_after_commit_throws()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		await using var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable);

		await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
		await unitOfWork.CommitAsync();

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => unitOfWork.FlushAsync());

		Assert.Contains("already committed", exception.Message);
	}

	[Fact]
	public async Task Rollback_after_rollback_throws()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		await using var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable);

		await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
		await unitOfWork.RollbackAsync();

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => unitOfWork.RollbackAsync());

		Assert.Contains("already rolled back", exception.Message);
	}

	[Fact]
	public async Task Rollback_discards_changes_immediately()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable);

		await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
		await unitOfWork.RollbackAsync();

		// изменения откачены в базе сразу, ещё до отпускания скоупа
		Assert.Null(await ReadOrderAsync(fixture, 1));

		// коннект свободен: откат завершил транзакцию, другой скоуп может открыть свою
		using (var readerScope = fixture.Provider.CreateScope())
		{
			var reader = readerScope.ServiceProvider.GetRequiredService<CatalogContext>();

			using (reader.Database.BeginTransaction())
			{
			}
		}

		await unitOfWork.DisposeAsync();
	}

	[Fact]
	public async Task Rollback_ends_scope_and_next_unit_of_work_starts_fresh()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		var first = factory.CreateUnitOfWork(IsolationLevel.Serializable);

		await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
		await first.RollbackAsync();

		await using var second = factory.CreateUnitOfWork(IsolationLevel.Serializable);

		// первый скоуп уже мёртв — его отпускание молча
		await first.DisposeAsync();

		await factory.Create<Order>().InsertAsync(new Order { Id = 2 });
		await second.CommitAsync();

		Assert.Null(await ReadOrderAsync(fixture, 1));
		Assert.NotNull(await ReadOrderAsync(fixture, 2));
	}

	[Fact]
	public async Task Dispose_without_commit_does_not_block_next_unit_of_work()
	{
		using var fixture = OrmFixture.Create();
		using var scope = fixture.Provider.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		using (factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<Order>().InsertAsync(new Order { Id = 1 });
		}

		await using (var unitOfWork = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<Order>().InsertAsync(new Order { Id = 2 });
			await unitOfWork.CommitAsync();
		}

		Assert.Null(await ReadOrderAsync(fixture, 1));
		Assert.NotNull(await ReadOrderAsync(fixture, 2));

		// второй скоуп завершён — мутации дальше бросают
		await Assert.ThrowsAsync<ObjectDisposedException>(
			() => factory.Create<Order>().InsertAsync(new Order { Id = 3 }));
	}

	private static async Task<Order?> ReadOrderAsync(OrmFixture fixture, int id)
	{
		using var scope = fixture.Provider.CreateScope();

		return await scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>()
			.Create<Order>()
			.GetAsync(id);
	}
}
