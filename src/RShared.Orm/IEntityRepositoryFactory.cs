using System.Data;

namespace RShared.Orm;

/// <summary>
/// Entity repository interface
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public interface IEntityRepositoryFactory
{
	public IUnitOfWork Create(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);
	public IEntityRepository<TEntity> Create<TEntity>()
		where TEntity : class;
}
