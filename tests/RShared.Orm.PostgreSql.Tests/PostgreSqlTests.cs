using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RShared.Orm;
using RShared.Orm.EntityFrameworkCore;
using Xunit;

namespace RShared.Orm.PostgreSql.Tests;

public class PostgreSqlTests
{
	private const string ConnectionString = "Host=localhost;Database=test;Username=postgres;Password=pass";

	[Fact]
	public void AddPostgreSqlContext_registers_npgsql_provider()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options => options.ConnectionString = ConnectionString));

		using var scope = provider.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();

		Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
	}

	[Fact]
	public void Snake_case_naming_is_default()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options => options.ConnectionString = ConnectionString));

		using var scope = provider.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();

		// EF10 плюрализует имена таблиц, snake_case применяется поверх: Order → orders
		Assert.Equal("orders", context.Model.FindEntityType(typeof(Order))!.GetTableName());
	}

	[Fact]
	public void Naming_convention_can_be_disabled()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options =>
			{
				options.ConnectionString = ConnectionString;
				options.UseSnakeCaseNaming = false;
			}));

		using var scope = provider.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();

		Assert.Equal("Orders", context.Model.FindEntityType(typeof(Order))!.GetTableName());
	}

	[Fact]
	public void Connection_string_builds_pooled_data_source()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options => options.ConnectionString = ConnectionString));

		Assert.NotNull(provider.GetRequiredService<NpgsqlDataSource>());
	}

	[Fact]
	public void Provided_data_source_is_registered_in_di()
	{
		using var dataSource = NpgsqlDataSource.Create(ConnectionString);

		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options => options.DataSource = dataSource));

		Assert.Same(dataSource, provider.GetRequiredService<NpgsqlDataSource>());
	}

	[Fact]
	public void Retry_is_off_by_default()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options => options.ConnectionString = ConnectionString));

		using var scope = provider.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();

		Assert.Equal("NpgsqlExecutionStrategy", context.Database.CreateExecutionStrategy().GetType().Name);
	}

	[Fact]
	public void Retry_can_be_enabled()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options =>
			{
				options.ConnectionString = ConnectionString;
				options.EnableRetryOnFailure = true;
			}));

		using var scope = provider.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();

		Assert.Equal("NpgsqlRetryingExecutionStrategy", context.Database.CreateExecutionStrategy().GetType().Name);
	}

	[Fact]
	public void AddPostgreSqlRepositories_wires_whole_stack()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlRepositories(
				options => options.ConnectionString = ConnectionString,
				typeof(CatalogContext), typeof(BillingContext)));

		using var scope = provider.CreateScope();

		// владение сущностями развязано по контекстам, модели построены, snake_case применён
		var factory = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>();

		Assert.NotNull(factory.Create<Order>());
		Assert.NotNull(factory.Create<Payment>());

		var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
		var billing = scope.ServiceProvider.GetRequiredService<BillingContext>();

		Assert.Equal("orders", catalog.Model.FindEntityType(typeof(Order))!.GetTableName());
		Assert.Equal("payments", billing.Model.FindEntityType(typeof(Payment))!.GetTableName());
	}

	[Fact]
	public void Escape_hatch_callbacks_are_invoked()
	{
		var npgsqlConfigured = false;
		var dbContextConfigured = false;

		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options =>
			{
				options.ConnectionString = ConnectionString;
				options.ConfigureNpgsql = _ => npgsqlConfigured = true;
				options.ConfigureDbContext = _ => dbContextConfigured = true;
			}));

		using var scope = provider.CreateScope();

		// лямбды настроек исполняются при создании контекста
		scope.ServiceProvider.GetRequiredService<CatalogContext>();

		Assert.True(npgsqlConfigured);
		Assert.True(dbContextConfigured);
	}

	[Fact]
	public void Missing_connection_settings_throw()
	{
		var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection()
			.AddPostgreSqlContext<CatalogContext>(options => { }));

		Assert.Contains("ConnectionString or DataSource is required", exception.Message);
	}

	[Fact]
	public void Ambiguous_connection_settings_throw()
	{
		var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection()
			.AddPostgreSqlContext<CatalogContext>(options =>
			{
				options.ConnectionString = ConnectionString;
				options.DataSource = NpgsqlDataSource.Create(ConnectionString);
			}));

		Assert.Contains("mutually exclusive", exception.Message);
	}

	[Fact]
	public void Abstract_context_type_throws()
	{
		var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection()
			.AddPostgreSqlContext<AbstractContext>(options => options.ConnectionString = ConnectionString));

		Assert.Contains("is not a concrete DbContext", exception.Message);
	}

	[Fact]
	public void Non_DbContext_type_throws()
	{
		var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection()
			.AddPostgreSqlRepositories(
				options => options.ConnectionString = ConnectionString,
				typeof(object)));

		Assert.Contains("is not a concrete DbContext", exception.Message);
	}

	private static ServiceProvider BuildProvider(Action<ServiceCollection> register)
	{
		var services = new ServiceCollection();

		register(services);

		return services.BuildServiceProvider();
	}

	public sealed class Order
	{
		public int Id { get; set; }

		public decimal Amount { get; set; }
	}

	public sealed class Payment
	{
		public int Id { get; set; }

		public int OrderId { get; set; }
	}

	public sealed class CatalogContext
		: DbContext
	{
		public CatalogContext(DbContextOptions<CatalogContext> options)
			: base(options)
		{
		}

		public DbSet<Order> Orders => Set<Order>();
	}

	public sealed class BillingContext
		: DbContext
	{
		public BillingContext(DbContextOptions<BillingContext> options)
			: base(options)
		{
		}

		public DbSet<Payment> Payments => Set<Payment>();
	}

	/// <summary>
	/// Абстрактный контекст — регистрация должна отвергать
	/// </summary>
	public abstract class AbstractContext
		: DbContext
	{
		public AbstractContext(DbContextOptions<AbstractContext> options)
			: base(options)
		{
		}
	}
	[Fact]
	public void Registration_is_scoped_by_default()
	{
		Assert.Equal(ContextRegistration.Scoped, new PostgreSqlOption().Registration);
	}

	[Fact]
	public void Pooled_registration_still_resolves_a_scoped_context()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options =>
			{
				options.ConnectionString = ConnectionString;
				options.Registration = ContextRegistration.Pooled;
			}));

		using var scope = provider.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();

		Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
	}

	[Fact]
	public void Factory_registration_provides_idbcontextfactory()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options =>
			{
				options.ConnectionString = ConnectionString;
				options.Registration = ContextRegistration.Factory;
			}));

		using var context = provider.GetRequiredService<IDbContextFactory<CatalogContext>>().CreateDbContext();

		Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
	}

	[Fact]
	public void Pooled_factory_registration_provides_idbcontextfactory()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options =>
			{
				options.ConnectionString = ConnectionString;
				options.Registration = ContextRegistration.PooledFactory;
			}));

		using var context = provider.GetRequiredService<IDbContextFactory<CatalogContext>>().CreateDbContext();

		Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
	}

	[Fact]
	public void Factory_registration_works_with_the_repository_stack()
	{
		var provider = BuildProvider(services => services
			.AddPostgreSqlContext<CatalogContext>(options =>
			{
				options.ConnectionString = ConnectionString;
				options.Registration = ContextRegistration.Factory;
			})
			.AddEntityRepositories(typeof(CatalogContext)));

		using var scope = provider.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IEntityRepositoryFactory>().Create<Order>();

		// реестр подхватил фабричный контекст: владение сущностями и IQueryable работают
		Assert.NotNull(repository);
		Assert.NotNull(repository.Query());
	}
}
