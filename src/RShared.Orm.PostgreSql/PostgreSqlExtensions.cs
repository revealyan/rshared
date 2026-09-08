using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using RShared.Orm.EntityFrameworkCore;

namespace RShared.Orm.PostgreSql;

/// <summary>
/// Registration of EF Core contexts over PostgreSQL wired into the entity repository stack
/// </summary>
public static class PostgreSqlExtensions
{
	/// <summary>
	/// Register a single DbContext over PostgreSQL: Npgsql provider, shared pooled data source,
	/// snake_case naming by default. For repositories over all contexts in one call
	/// use <see cref="AddPostgreSqlRepositories"/> instead.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configure">PostgreSQL options</param>
	public static IServiceCollection AddPostgreSqlContext<TContext>(this IServiceCollection services, Action<PostgreSqlOption> configure)
		where TContext : DbContext
	{
		var option = BuildOption(configure);

		RequireConcreteContext(typeof(TContext));
		RegisterContext<TContext>(services, option);

		return services;
	}

	/// <summary>
	/// Register contexts over PostgreSQL and entity repositories over them — the one-line host wiring.
	/// All contexts share one data source and the same options; for per-context settings
	/// use <see cref="AddPostgreSqlContext{TContext}"/> per context plus AddEntityRepositories.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configure">Common options for all contexts</param>
	/// <param name="contextTypes">DbContext types to register and serve</param>
	public static IServiceCollection AddPostgreSqlRepositories(this IServiceCollection services, Action<PostgreSqlOption> configure, params Type[] contextTypes)
	{
		var option = BuildOption(configure);

		foreach (var contextType in contextTypes)
		{
			RequireConcreteContext(contextType);

			// AddDbContext у EF существует только в generic-виде — регистрируем через generic-метод
			typeof(PostgreSqlExtensions)
				.GetMethod(nameof(RegisterContext), BindingFlags.NonPublic | BindingFlags.Static)!
				.MakeGenericMethod(contextType)
				.Invoke(null, [services, option]);
		}

		return services.AddEntityRepositories(contextTypes);
	}

	private static PostgreSqlOption BuildOption(Action<PostgreSqlOption> configure)
	{
		var option = new PostgreSqlOption();
		configure(option);

		if (option.DataSource is null && string.IsNullOrWhiteSpace(option.ConnectionString))
		{
			throw new ArgumentException("ConnectionString or DataSource is required", nameof(configure));
		}

		if (option.DataSource is not null && !string.IsNullOrWhiteSpace(option.ConnectionString))
		{
			throw new ArgumentException("ConnectionString and DataSource are mutually exclusive", nameof(configure));
		}

		return option;
	}

	private static void RequireConcreteContext(Type contextType)
	{
		if (contextType.IsAbstract || !typeof(DbContext).IsAssignableFrom(contextType))
		{
			throw new ArgumentException($"\"{contextType.Name}\" is not a concrete DbContext");
		}
	}

	private static void RegisterContext<TContext>(IServiceCollection services, PostgreSqlOption option)
		where TContext : DbContext
	{
		var dataSource = option.DataSource ?? new NpgsqlDataSourceBuilder(option.ConnectionString).Build();
		services.TryAddSingleton(dataSource);

		services.AddDbContext<TContext>(builder =>
		{
			builder.UseNpgsql(dataSource, npgsql =>
			{
				if (option.EnableRetryOnFailure)
				{
					npgsql.EnableRetryOnFailure();
				}

				option.ConfigureNpgsql?.Invoke(npgsql);
			});

			if (option.UseSnakeCaseNaming)
			{
				builder.UseSnakeCaseNamingConvention();
			}

			option.ConfigureDbContext?.Invoke(builder);
		});
	}
}
