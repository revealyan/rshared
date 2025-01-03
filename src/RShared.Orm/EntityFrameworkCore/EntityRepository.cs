using Microsoft.EntityFrameworkCore;

namespace RShared.Orm.EntityFrameworkCore;

internal class EntityRepository<TEntity>
	: IEntityRepository<TEntity>
	where TEntity : class
{
	/// <summary>
	/// Entity framework context
	/// </summary>
	protected readonly DbContext Context;


	/// <summary>
	/// Create instance of entity repository
	/// </summary>
	/// <param name="context">EF context</param>
	public EntityRepository(DbContext context)
	{
		Context = context;
	}



	/// <inheritdoc />
	public IQueryable<TEntity> Query()
	{
		return Context.Set<TEntity>();
	}

	/// <inheritdoc />
	public async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
	{
		return (await Context.Set<TEntity>().AddAsync(entity, cancellationToken)).Entity;
	}

	/// <inheritdoc />
	public async Task<TEntity?> GetAsync<TKey>(TKey key, CancellationToken cancellationToken = default)
	{
		return await Context.Set<TEntity>().FindAsync(key, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
	{
		if (await Context.Set<TEntity>().ContainsAsync(entity, cancellationToken))
		{
			return Context.Set<TEntity>().Update(entity).Entity;
		}
		return (await Context.Set<TEntity>().AddAsync(entity, cancellationToken)).Entity;
	}

	/// <inheritdoc />
	public Task<TEntity?> DeleteAsync(TEntity entity, CancellationToken _ = default)
	{
		return Task.FromResult<TEntity?>(Context.Set<TEntity>().Remove(entity).Entity);
	}
}
