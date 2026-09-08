using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace RShared.Orm.PostgreSql;

/// <summary>
/// Options for PostgreSQL context registration
/// </summary>
public sealed class PostgreSqlOption
{
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
