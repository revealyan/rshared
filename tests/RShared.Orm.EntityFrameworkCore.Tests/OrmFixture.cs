using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RShared.Orm.EntityFrameworkCore.Tests;

/// <summary>
/// Провайдер на SQLite in-memory: свой коннект на каждый контекст,
/// чтобы транзакции разных контекстов не дрались за один коннект.
/// SQLite не понимает ReadCommitted, поэтому в тестах используется Serializable.
/// </summary>
internal sealed class OrmFixture
	: IDisposable
{
	private readonly List<SqliteConnection> _connections = new();

	public ServiceProvider Provider { get; private set; } = null!;

	public static OrmFixture Create()
	{
		var fixture = new OrmFixture();

		var services = new ServiceCollection()
			.AddEntityRepositories(typeof(CatalogContext), typeof(BillingContext));

		fixture.AddSqliteContext<CatalogContext>(services);
		fixture.AddSqliteContext<BillingContext>(services);

		fixture.Provider = services.BuildServiceProvider();

		using (var scope = fixture.Provider.CreateScope())
		{
			scope.ServiceProvider.GetRequiredService<CatalogContext>().Database.EnsureCreated();
			scope.ServiceProvider.GetRequiredService<BillingContext>().Database.EnsureCreated();
		}

		return fixture;
	}

	public SqliteConnection CreateConnection()
	{
		var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		_connections.Add(connection);

		return connection;
	}

	private void AddSqliteContext<TContext>(IServiceCollection services)
		where TContext : DbContext
	{
		// коннект захватывается один раз на регистрацию, не на каждый контекст
		var connection = CreateConnection();

		services.AddDbContext<TContext>(options => options.UseSqlite(connection));
	}

	public void Dispose()
	{
		Provider.Dispose();

		foreach (var connection in _connections)
		{
			connection.Dispose();
		}
	}
}
