using System.Data;

namespace RShared.Orm;

/// <summary>
/// Entity repository factory: entry point for repositories and units of work
/// </summary>
public interface IEntityRepositoryFactory
{
	/// <summary>
	/// Create unit of work. Transaction begins lazily, on first entity mutation
	/// made while the unit of work is active — and only in the owning context.
	/// While a unit of work is active, nested ones join it: their Commit is a checkpoint flush,
	/// the root's Commit commits the shared scope; isolation level of the root applies
	/// </summary>
	/// <param name="isolationLevel">Transaction isolation level</param>
	/// <returns>Unit of work</returns>
	IUnitOfWork CreateUnitOfWork(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

	/// <summary>
	/// Create repository for <typeparamref name="TEntity"/>, bound to its owning context.
	/// Repositories do not save changes themselves — mutations are saved by a unit of work
	/// </summary>
	/// <typeparam name="TEntity">Entity type</typeparam>
	/// <returns>Entity repository</returns>
	IEntityRepository<TEntity> Create<TEntity>()
		where TEntity : class;
}
