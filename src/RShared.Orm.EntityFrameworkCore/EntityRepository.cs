using Microsoft.EntityFrameworkCore;

namespace RShared.Orm.EntityFrameworkCore;

/// <summary>
/// Репозиторий, привязанный к контексту EF
/// </summary>
internal interface IContextBoundRepository
{
	/// <summary>
	/// EF контекст репозитория
	/// </summary>
	DbContext Context { get; }
}

internal class EntityRepository<TEntity>
	: IEntityRepository<TEntity>, IContextBoundRepository
	where TEntity : class
{
	/// <summary>
	/// Entity framework context
	/// </summary>
	protected readonly DbContext Context;

	/// <summary>
	/// Вызывается перед мутацией: репозиторий регистрирует свой контекст в активном unit of work
	/// </summary>
	private readonly Action? _onMutate;

	/// <summary>
	/// Create instance of entity repository
	/// </summary>
	/// <param name="context">EF context</param>
	/// <param name="onMutate">Mutation callback, invoked before each write operation</param>
	public EntityRepository(DbContext context, Action? onMutate = null)
	{
		Context = context;
		_onMutate = onMutate;
	}

	DbContext IContextBoundRepository.Context => Context;

	/// <inheritdoc />
	public async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
	{
		_onMutate?.Invoke();

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
		_onMutate?.Invoke();

		if (await Context.Set<TEntity>().ContainsAsync(entity, cancellationToken))
		{
			return Context.Set<TEntity>().Update(entity).Entity;
		}

		return (await Context.Set<TEntity>().AddAsync(entity, cancellationToken)).Entity;
	}

	/// <inheritdoc />
	public Task<TEntity?> DeleteAsync(TEntity entity, CancellationToken _ = default)
	{
		_onMutate?.Invoke();

		return Task.FromResult<TEntity?>(Context.Set<TEntity>().Remove(entity).Entity);
	}
}
