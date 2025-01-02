namespace RShared.Orm;

/// <summary>
/// Entity repository interface
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public interface IEntityRepository<TEntity>
	: IQueryable<TEntity>
	where TEntity : class
{
	/// <summary>
	/// Insert entity to repository
	/// </summary>
	/// <param name="entity">Entity</param>
	/// <param name="cancellationToken">Operation cancellation token</param>
	/// <returns>Inserted entity</returns>
	public Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get entity from repository by key
	/// </summary>
	/// <typeparam name="TKey">Key type</typeparam>
	/// <param name="key">Key</param>
	/// <param name="cancellationToken">Operation cancellation token</param>
	/// <returns>Entity if it present</returns>
	public Task<TEntity?> GetAsync<TKey>(TKey key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Insert if not preset, or update entity
	/// </summary>
	/// <param name="entity">Entoty</param>
	/// <param name="cancellationToken">Operation cancellation token</param>
	/// <returns>Updated entity</returns>
	public Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deleted entity from repository, if it present
	/// </summary>
	/// <param name="entity">Entity</param>
	/// <param name="cancellationToken">Operation cancellation token</param>
	/// <returns>Deleted entity, if it present in repository</returns>
	public Task<TEntity?> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
}
