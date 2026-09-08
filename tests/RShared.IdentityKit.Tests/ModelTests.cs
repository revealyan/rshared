using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RShared.IdentityKit;
using RShared.Orm;
using RShared.Orm.EntityFrameworkCore;

using Xunit;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// EF-модель: имена таблиц, индексы, конвертация enum'ов, отсутствие NormalizedEmail-дубля,
/// поведение колонок на PostgreSQL (модельные тесты на Npgsql без сервера)
/// </summary>
public sealed class ModelTests
{
	private static IModel SqliteModel()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		using var ctx = new KitContext(new DbContextOptionsBuilder<KitContext>().UseSqlite(connection).Options);
		return ctx.Model;
	}

	private static IEntityType Entity(IModel model, Type type)
	{
		return model.GetEntityTypes().Single(e => e.ClrType == type);
	}

	[Fact]
	public void Table_names_are_pinned()
	{
		var model = SqliteModel();

		Assert.Equal("users", Entity(model, typeof(IdentityKitUser)).GetTableName());
		Assert.Equal("external_accounts", Entity(model, typeof(ExternalAccount)).GetTableName());
		Assert.Equal("one_time_codes", Entity(model, typeof(OneTimeCode)).GetTableName());

		// заодно уникальные индексы: Stryker мапит модельные строки в основном на этот тест
		var users = Entity(model, typeof(IdentityKitUser));
		var accounts = Entity(model, typeof(ExternalAccount));
		Assert.Contains(users.GetIndexes(), i => i.IsUnique
			&& i.Properties.Select(p => p.Name).SequenceEqual([nameof(IdentityKitUser.Email)]));
		Assert.Contains(accounts.GetIndexes(), i => i.IsUnique
			&& i.Properties.Select(p => p.Name).SequenceEqual([nameof(ExternalAccount.Provider), nameof(ExternalAccount.ExternalId)]));
	}

	[Fact]
	public void Email_is_unique_and_not_duplicated_by_normalized_copy()
	{
		var model = SqliteModel();
		var user = Entity(model, typeof(IdentityKitUser));

		Assert.Contains(user.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(IdentityKitUser.Email)]) && i.IsUnique);
		Assert.DoesNotContain(user.GetProperties(), p => p.Name.Contains("Normalized", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void External_account_identity_is_unique()
	{
		var model = SqliteModel();
		var account = Entity(model, typeof(ExternalAccount));

		Assert.Contains(account.GetIndexes(), i => i.IsUnique
			&& i.Properties.Select(p => p.Name).SequenceEqual([nameof(ExternalAccount.Provider), nameof(ExternalAccount.ExternalId)]));
	}

	[Fact]
	public void Purpose_and_channel_stored_as_strings()
	{
		var model = SqliteModel();
		var code = Entity(model, typeof(OneTimeCode));

		var purpose = code.GetProperties().Single(p => p.Name == nameof(OneTimeCode.Purpose));
		var channel = code.GetProperties().Single(p => p.Name == nameof(OneTimeCode.Channel));
		Assert.Equal("TEXT", purpose.GetColumnType());
		Assert.Equal("TEXT", channel.GetColumnType());
	}

	[Fact]
	public void Code_hash_has_an_index()
	{
		var model = SqliteModel();
		var code = Entity(model, typeof(OneTimeCode));

		Assert.Contains(code.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(OneTimeCode.CodeHash)]));
	}

	[Fact]
	public async Task Roles_round_trip()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var users = scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>();

		await using (var uow = scope.Get<IEntityRepositoryFactory>().CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await users.InsertAsync(new IdentityKitUser
			{
				Id = Guid.CreateVersion7(),
				Email = "a@b.c",
				Roles = ["admin", "boss"],
				SecurityStamp = "s1",
				CreatedAt = DateTime.UtcNow,
			});
			await uow.CommitAsync();
		}

		var loaded = await users.Query().SingleAsync(u => u.Email == "a@b.c");
		Assert.Equal(["admin", "boss"], loaded.Roles);
	}

	[Fact]
	public void Npgsql_model_maps_roles_to_text_array()
	{
		using var ctx = new KitContext(new DbContextOptionsBuilder<KitContext>()
			.UseNpgsql("Host=localhost;Database=kit;Username=kit;Password=kit").Options);
		var property = Entity(ctx.Model, typeof(IdentityKitUser)).GetProperties()
			.Single(p => p.Name == nameof(IdentityKitUser.Roles));

		Assert.Equal("text[]", property.GetColumnType());
	}

	[Fact]
	public void Npgsql_model_maps_email_to_text()
	{
		using var ctx = new KitContext(new DbContextOptionsBuilder<KitContext>()
			.UseNpgsql("Host=localhost;Database=kit;Username=kit;Password=kit").Options);
		var property = Entity(ctx.Model, typeof(IdentityKitUser)).GetProperties()
			.Single(p => p.Name == nameof(IdentityKitUser.Email));

		Assert.Equal("text", property.GetColumnType());
	}

	[Fact]
	public async Task Duplicate_email_is_rejected_by_the_database()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var factory = scope.Get<IEntityRepositoryFactory>();

		await using (var uow = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<IdentityKitUser>().InsertAsync(NewUser("a@b.c"));
			await uow.CommitAsync();
		}

		await using (var uow = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<IdentityKitUser>().InsertAsync(NewUser("a@b.c"));
			await Assert.ThrowsAsync<DbUpdateException>(() => uow.CommitAsync());
		}
	}

	[Fact]
	public async Task Duplicate_external_account_is_rejected_by_the_database()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var factory = scope.Get<IEntityRepositoryFactory>();
		var user = NewUser("a@b.c");

		await using (var uow = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<IdentityKitUser>().InsertAsync(user);
			await factory.Create<ExternalAccount>().InsertAsync(NewAccount("google", "g1", user.Id));
			await uow.CommitAsync();
		}

		await using (var uow = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<ExternalAccount>().InsertAsync(NewAccount("google", "g1", user.Id));
			await Assert.ThrowsAsync<DbUpdateException>(() => uow.CommitAsync());
		}
	}

	private static IdentityKitUser NewUser(string email)
	{
		return new IdentityKitUser
		{
			Id = Guid.CreateVersion7(),
			Email = email,
			SecurityStamp = Stamp.New(),
			CreatedAt = DateTime.UtcNow,
		};
	}

	private static ExternalAccount NewAccount(string provider, string externalId, Guid userId)
	{
		return new ExternalAccount
		{
			Id = Guid.CreateVersion7(),
			Provider = provider,
			ExternalId = externalId,
			UserId = userId,
			CreatedAt = DateTime.UtcNow,
		};
	}
}
