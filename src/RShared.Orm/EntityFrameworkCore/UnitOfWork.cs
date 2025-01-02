using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RShared.Orm.EntityFrameworkCore;

internal sealed class UnitOfWork
	: IUnitOfWork
{
	private readonly DbContext _context;
	private readonly IDbContextTransaction _transaction;

	public UnitOfWork(DbContext context)
	{
		_context = context;
		_transaction = _context.Database.CurrentTransaction ?? throw new ArgumentNullException(nameof(context.Database.CurrentTransaction));
	}
	public async Task CommitAsync(CancellationToken cancellationToken = default)
	{
		await FlushAsync(cancellationToken);
		await _transaction.CommitAsync(cancellationToken);
	}

	public Task FlushAsync(CancellationToken cancellationToken = default)
	{
		return _context.SaveChangesAsync(cancellationToken);
	}

	public Task RollbackAsync(CancellationToken cancellationToken = default)
	{
		return _transaction.RollbackAsync(cancellationToken);
	}

	public void Dispose()
	{
		_transaction?.Dispose();
	}
}
