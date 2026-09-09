using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace RShared.Orm.PostgreSql;

/// <summary>
/// How contexts are registered in DI. All four EF Core registration modes work with the
/// repository stack: the registry resolves scoped contexts directly and picks up
/// an <c>IDbContextFactory&lt;T&gt;</c> when present.
/// </summary>
public enum ContextRegistration
{
	/// <summary>
	/// Scoped context per unit of work (AddDbContext). The default.
	/// </summary>
	Scoped,

	/// <summary>
	/// Pooled scoped contexts (AddDbContextPool): the same contract, cheaper on the hot path.
	/// The context must hold no state of its own between uses.
	/// </summary>
	Pooled,

	/// <summary>
	/// Singleton factory of short-lived contexts (AddDbContextFactory):
	/// background workers and parallel units of work.
	/// </summary>
	Factory,

	/// <summary>
	/// Factory with pooling (AddPooledDbContextFactory).
	/// </summary>
	PooledFactory,
}

/// <summary>
/// Options for PostgreSQL context registration
/// </summary>
public sealed class PostgreSqlOption
{
	/// <summary>
	/// DI registration mode for contexts. Scoped by default.
	/// </summary>
	public ContextRegistration Registration { get; set; } = ContextRegistration.Scoped;
	/// <summary>
	/// Connection string. Required unless <see cref="DataSource"/> is set;
	/// a pooled <see cref="NpgsqlDataSource"/> is built from it and shared by all contexts.
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>
	/// Ready data source. When set, <see cref="ConnectionString"/> must be left null.
	/// Owned by the caller: it is registered in DI but never disposed by it.
	/// </summary>
	public NpgsqlDataSource? DataSource { get; set; }

	/// <summary>
	/// snake_case convention for tables and columns (EFCore.NamingConventions). On by default.
	/// </summary>
	public bool UseSnakeCaseNaming { get; set; } = true;

	/// <summary>
	/// Retry on transient failures. Off by default: the retrying execution strategy
	/// is incompatible with explicit transactions, and the unit of work opens them.
	/// Enable only for contexts used without the unit of work.
	/// </summary>
	public bool EnableRetryOnFailure { get; set; }

	/// <summary>
	/// Raw escape hatch, applied after all conventions above
	/// </summary>
	public Action<DbContextOptionsBuilder>? ConfigureDbContext { get; set; }

	/// <summary>
	/// Provider-specific settings (enum mapping, timeouts), applied inside UseNpgsql
	/// </summary>
	public Action<NpgsqlDbContextOptionsBuilder>? ConfigureNpgsql { get; set; }
}
