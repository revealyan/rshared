using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RShared.Orm.EntityFrameworkCore;
using System.Data;

namespace RShared.Orm;

/// <summary>
/// Entity repository interface
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public class EntityRepositoryFactory<TDbContext>
	: IEntityRepositoryFactory, IDisposable
	where TDbContext : DbContext
{
	private readonly IDbContextFactory<TDbContext>? _contextFactory;
	private readonly TDbContext _context;
	private readonly IServiceProvider _serviceProvider;
	private UnitOfWork? _unitOfWork;

	public EntityRepositoryFactory(TDbContext context, IServiceProvider serviceProvider)
	{
		_contextFactory = null;
		_context = context;
		_serviceProvider = serviceProvider;
	}

	public EntityRepositoryFactory(IDbContextFactory<TDbContext> dbContextFactory, IServiceProvider serviceProvider)
	{
		_contextFactory = dbContextFactory;
		_context = dbContextFactory.CreateDbContext();
		_serviceProvider = serviceProvider;
	}

	public IUnitOfWork Create(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
	{
		_context.Database.BeginTransaction();
		_unitOfWork = new UnitOfWork(_context);
		return _unitOfWork;
	}

	public IEntityRepository<TEntity> Create<TEntity>()
		where TEntity : class
	{
		return _serviceProvider.GetService<IEntityRepository<TEntity>>() ?? new EntityRepository<TEntity>(_context);
	}

	public void Dispose()
	{
		if (_contextFactory is not null)
		{
			_context.Dispose();
		}
		_unitOfWork?.Dispose();
	}
}
