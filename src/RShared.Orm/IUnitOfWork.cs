namespace RShared.Orm;

/// <summary>
/// Unit of work interface, represent atomic transaction
/// </summary>
public interface IUnitOfWork
	: IDisposable
{
	/// <summary>
	/// Flush operation to transaction async
	/// </summary>
	/// <param name="cancellationToken">Operation cancellation token</param>
	/// <returns>Task of flushing</returns>
	Task FlushAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Commit changes async
	/// </summary>
	/// <param name="cancellationToken">Operation cancellation token</param>
	/// <returns>Task of commiting</returns>
	Task CommitAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Cancel changes async
	/// </summary>
	/// <param name="cancellationToken">Operation cancellation token</param>
	/// <returns>Task of rollbacking</returns>
	Task RollbackAsync(CancellationToken cancellationToken = default);
}
