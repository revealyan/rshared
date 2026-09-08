using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RShared.Orm.EntityFrameworkCore;

/// <summary>
/// Реестр владения: тип сущности → тип контекста, плюс резолверы контекстов.
/// Владение определяется моделью контекста — что сконфигурировано, тем он и владеет.
/// Резолверы готовятся один раз при регистрации; карта владельцев строится один раз —
/// при старте хоста (warm-up) либо при первом обращении.
/// </summary>
internal sealed class EntityRepositoryRegistry
{
	private readonly Type[] _contextTypes;
	private readonly Dictionary<Type, Func<IServiceProvider, (DbContext Context, bool Owned)>> _resolvers = new();
	private readonly object _lock = new();
	private volatile Dictionary<Type, Type>? _owners;

	/// <summary>
	/// Create registry of <paramref name="contextTypes"/>. Resolvers are prepared here, at registration time
	/// </summary>
	/// <exception cref="ArgumentException">Throws when types are empty or not concrete DbContext</exception>
	public EntityRepositoryRegistry(params Type[] contextTypes)
	{
		_contextTypes = contextTypes.Distinct().ToArray();

		if (_contextTypes.Length == 0)
		{
			throw new ArgumentException("At least one DbContext type is required", nameof(contextTypes));
		}

		foreach (var contextType in _contextTypes)
		{
			if (contextType.IsAbstract || !typeof(DbContext).IsAssignableFrom(contextType))
			{
				throw new ArgumentException($@"""{contextType.FullName}"" is not a concrete DbContext", nameof(contextTypes));
			}

			_resolvers[contextType] = CreateResolver(contextType);
		}
	}

	/// <summary>
	/// Прогреть карту владельцев по моделям всех контекстов. Повторный вызов — no-op
	/// </summary>
	public void WarmUp(IServiceProvider provider)
	{
		_ = GetOwners(provider);
	}

	/// <summary>
	/// Тип контекста, владеющего сущностью <paramref name="entityType"/>
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Throws when no context owns the entity or two contexts claim the same entity
	/// </exception>
	public Type GetContextType(Type entityType, IServiceProvider provider)
	{
		var owners = GetOwners(provider);

		return owners.TryGetValue(entityType, out var contextType)
			? contextType
			: throw new InvalidOperationException(
				$@"No registered context owns entity ""{entityType.FullName}"" — add it to a context model");
	}

	/// <summary>
	/// Резолв инстанса контекста: через IDbContextFactory (владеем мы) или из скоупа (владеет скоуп)
	/// </summary>
	public (DbContext Context, bool Owned) ResolveContext(Type contextType, IServiceProvider provider)
	{
		return _resolvers[contextType](provider);
	}

	private Dictionary<Type, Type> GetOwners(IServiceProvider provider)
	{
		// Stryker disable once NullCoalescing: эквивалент — BuildOwners внутри lock вернёт готовый _owners
		return _owners ?? BuildOwners(provider);
	}

	private Dictionary<Type, Type> BuildOwners(IServiceProvider provider)
	{
		lock (_lock)
		{
			if (_owners is not null)
			{
				return _owners;
			}

			var owners = new Dictionary<Type, Type>();

			foreach (var contextType in _contextTypes)
			{
				var (context, owned) = ResolveContext(contextType, provider);

				try
				{
					foreach (var entityType in context.Model.GetEntityTypes())
					{
						var clrType = entityType.ClrType;

						if (owners.TryGetValue(clrType, out var owner))
						{
							throw new InvalidOperationException(
								$@"Entity ""{clrType.FullName}"" is owned by both ""{owner.FullName}"" and ""{contextType.FullName}""");
						}

						owners[clrType] = contextType;
					}
				}
				finally
				{
					// Stryker disable Block, Statement: диспоз прогревочного контекста недоступен для наблюдения извне — только утечка, не поведение
					if (owned)
					{
						context.Dispose();
					}
				}
			}

			_owners = owners;

			return owners;
		}
	}

	/// <summary>
	/// Собрать резолвер контекста: типизированная лямбда, без рефлексии на каждый вызов
	/// </summary>
	private static Func<IServiceProvider, (DbContext Context, bool Owned)> CreateResolver(Type contextType)
	{
		var resolver = typeof(EntityRepositoryRegistry)
			.GetMethod(nameof(ResolverFor), BindingFlags.NonPublic | BindingFlags.Static)!
			.MakeGenericMethod(contextType);

		return (Func<IServiceProvider, (DbContext Context, bool Owned)>)resolver.Invoke(null, null)!;
	}

	private static Func<IServiceProvider, (DbContext Context, bool Owned)> ResolverFor<TContext>()
		where TContext : DbContext
	{
		return provider =>
		{
			if (provider.GetService<IDbContextFactory<TContext>>() is { } contextFactory)
			{
				return (contextFactory.CreateDbContext(), true);
			}

			if (provider.GetService<TContext>() is DbContext scoped)
			{
				return (scoped, false);
			}

			throw new NotSupportedException(
				$@"For context ""{typeof(TContext).FullName}"" neither ""{typeof(IDbContextFactory<TContext>)}"" nor the context itself is registered");
		};
	}
}
