using System.Data;
using Microsoft.EntityFrameworkCore;
using RShared.Orm;

namespace RShared.Orm.EntityFrameworkCore;

/// <summary>
/// Фабрика репозиториев: по типу сущности находит контекст-владелец и выдаёт репозиторий.
/// Контексты резолвятся один раз на скоуп; созданные через IDbContextFactory диспозит сама фабрика.
/// Держит активный unit of work: репозитории при первой мутации регистрируют свой контекст в нём.
/// Пока он жив, новые unit of work присоединяются к нему (вложенность без падения).
/// </summary>
internal sealed class EntityRepositoryFactory
	: IEntityRepositoryFactory, IDisposable
{
	private readonly EntityRepositoryRegistry _registry;
	private readonly IServiceProvider _provider;
	private readonly Dictionary<Type, (DbContext Context, bool Owned)> _contexts = new();
	private UnitOfWorkCore? _currentCore;

	public EntityRepositoryFactory(EntityRepositoryRegistry registry, IServiceProvider provider)
	{
		_registry = registry;
		_provider = provider;
	}

	/// <inheritdoc />
	public IUnitOfWork CreateUnitOfWork(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
	{
		if (_currentCore is { IsAlive: true } core)
		{
			core.AddReference();

			return new UnitOfWork(core, root: false);
		}

		// мёртвый скоуп остаётся в указателе: мутации после его завершения
		// получают внятную ошибку вместо молчаливой потери, а новый корень его заменит
		var fresh = new UnitOfWorkCore(isolationLevel);

		_currentCore = fresh;

		return new UnitOfWork(fresh, root: true);
	}

	/// <inheritdoc />
	public IEntityRepository<TEntity> Create<TEntity>()
		where TEntity : class
	{
		var context = ContextFor<TEntity>();

		return new EntityRepository<TEntity>(context, () => _currentCore?.Enlist(context));
	}

	/// <summary>
	/// Контекст-владелец сущности: реестр + резолв + кэш на скоуп
	/// </summary>
	internal DbContext ContextFor<TEntity>()
		where TEntity : class
	{
		var contextType = _registry.GetContextType(typeof(TEntity), _provider);

		return Resolve(contextType).Context;
	}

	private (DbContext Context, bool Owned) Resolve(Type contextType)
	{
		if (_contexts.TryGetValue(contextType, out var cached))
		{
			return cached;
		}

		var resolved = _registry.ResolveContext(contextType, _provider);

		_contexts[contextType] = resolved;

		return resolved;
	}

	public void Dispose()
	{
		foreach (var (context, owned) in _contexts.Values)
		{
			if (owned)
			{
				context.Dispose();
			}
		}
	}
}
