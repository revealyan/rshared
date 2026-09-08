using Microsoft.EntityFrameworkCore;
using RShared.Orm;

namespace RShared.Orm.EntityFrameworkCore;

/// <summary>
/// EF-specific queryable access to repositories — deliberately outside the contracts:
/// IQueryable carries provider expectations, other implementations may not have it
/// </summary>
public static class EntityRepositoryExtensions
{
	/// <summary>
	/// Create IQueryable for entity
	/// </summary>
	/// <typeparam name="TEntity">Entity type</typeparam>
	/// <param name="repository">Entity repository</param>
	/// <returns>IQueryable instance</returns>
	/// <exception cref="NotSupportedException">Throws when repository is not EF-bound</exception>
	public static IQueryable<TEntity> Query<TEntity>(this IEntityRepository<TEntity> repository)
		where TEntity : class
	{
		return repository is IContextBoundRepository bound
			? bound.Context.Set<TEntity>()
			: throw new NotSupportedException($@"Query is available for EF repositories only, ""{repository.GetType().FullName}"" is not");
	}
}
