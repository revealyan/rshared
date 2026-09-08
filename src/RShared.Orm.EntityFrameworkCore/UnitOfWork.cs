using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RShared.Orm.EntityFrameworkCore;

/// <summary>
/// Общее состояние скоупа unit of work: транзакции enlist-контекстов и терминальное состояние.
/// Один скоуп — одна транзакция на каждый затронутый контекст; скоуп живёт, пока жив хоть один handle.
/// Это не распределённая транзакция: коммиты идут последовательно, без 2PC —
/// упавший посередине CommitAsync оставит ранние контексты закоммиченными.
/// </summary>
internal sealed class UnitOfWorkCore
{
	private enum ScopeState
	{
		Active,
		Committed,
		RolledBack,
		Disposed
	}

	private readonly IsolationLevel _isolationLevel;
	private readonly Dictionary<DbContext, IDbContextTransaction> _transactions = new();
	private ScopeState _state = ScopeState.Active;
	private int _references = 1;

	public UnitOfWorkCore(IsolationLevel isolationLevel)
	{
		_isolationLevel = isolationLevel;
	}

	/// <summary>
	/// Скоуп ещё может принимать работу
	/// </summary>
	public bool IsAlive => _state == ScopeState.Active;

	/// <summary>
	/// Добавить ссылку: вложенный unit of work присоединяется к скоупу
	/// </summary>
	public void AddReference()
	{
		// Stryker disable once Statement: недостижимо — фабрика зовёт AddReference только у живого (IsAlive) скоупа
		ThrowIfNotActive();

		_references++;
	}

	/// <summary>
	/// Зарегистрировать контекст и открыть в нём транзакцию. Повторная регистрация — no-op
	/// </summary>
	public void Enlist(DbContext context)
	{
		ThrowIfNotActive();

		if (!_transactions.ContainsKey(context))
		{
			_transactions[context] = context.Database.BeginTransaction(_isolationLevel);
		}
	}

	public async Task FlushAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfNotActive();

		foreach (var context in _transactions.Keys)
		{
			await context.SaveChangesAsync(cancellationToken);
		}
	}

	public async Task CommitAsync(CancellationToken cancellationToken = default)
	{
		await FlushAsync(cancellationToken);

		foreach (var transaction in _transactions.Values)
		{
			await transaction.CommitAsync(cancellationToken);
		}

		Finish(ScopeState.Committed);
	}

	public async Task RollbackAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfNotActive();

		// откат = вся работа скоупа отброшена: зачистить трекер, чтобы откаченные
		// сущности не «воскресли» коммитом следующего unit of work этого скоупа
		foreach (var (context, transaction) in _transactions)
		{
			await transaction.RollbackAsync(cancellationToken);
			// Stryker disable once Statement: эквивалент — после отката скоуп недоступен для flush, финальную чистку делает Release→Discard
			context.ChangeTracker.Clear();
		}

		Finish(ScopeState.RolledBack);
	}

	/// <summary>
	/// Отпустить ссылку: скоуп умирает вместе с последней.
	/// Корень, отпущенный раньше вложенных, откатывает скоуп и кидает —
	/// закоммитить его уже некому, молча терять работу нельзя
	/// </summary>
	public void Release(bool root)
	{
		if (_state == ScopeState.Disposed)
		{
			return;
		}

		_references--;

		if (_references > 0)
		{
			if (!root)
			{
				return;
			}

			TearDown();
			Finish(ScopeState.RolledBack);

			throw new InvalidOperationException(
				"Root unit of work is disposed while nested ones are still active — scope rolled back");
		}

		TearDown();
		Finish(ScopeState.Disposed);
	}

	/// <summary>
	/// Демонтаж скоупа: транзакции до-диспозятся (незакоммиченные откатятся провайдером),
	/// трекеры enlisted-контекстов чистятся — контекст остаётся чистым для следующего unit of work
	/// </summary>
	private void TearDown()
	{
		foreach (var (context, transaction) in _transactions)
		{
			transaction.Dispose();
			context.ChangeTracker.Clear();
		}
	}

	private void Finish(ScopeState state)
	{
		_state = state;
	}

	private void ThrowIfNotActive()
	{
		switch (_state)
		{
			case ScopeState.Disposed:
				throw new ObjectDisposedException(nameof(UnitOfWorkCore));
			case ScopeState.Committed:
				throw new InvalidOperationException("Unit of work is already committed");
			case ScopeState.RolledBack:
				throw new InvalidOperationException("Unit of work is already rolled back");
		}
	}
}

/// <summary>
/// Handle над общим скоупом. Корневой handle делает настоящий COMMIT,
/// вложенный — flush-чекпоинт: его работа фиксируется коммитом корня.
/// </summary>
internal sealed class UnitOfWork
	: IUnitOfWork
{
	private readonly UnitOfWorkCore _core;
	private readonly bool _root;
	private bool _disposed;

	public UnitOfWork(UnitOfWorkCore core, bool root)
	{
		_core = core;
		_root = root;
	}

	public Task FlushAsync(CancellationToken cancellationToken = default)
	{
		return _core.FlushAsync(cancellationToken);
	}

	public Task CommitAsync(CancellationToken cancellationToken = default)
	{
		return _root
			? _core.CommitAsync(cancellationToken)
			: _core.FlushAsync(cancellationToken);
	}

	public Task RollbackAsync(CancellationToken cancellationToken = default)
	{
		return _core.RollbackAsync(cancellationToken);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		// Stryker disable once Boolean: повторный Dispose безвреден — Release выходит по Disposed-состоянию скоупа
		_disposed = true;

		_core.Release(_root);
	}

	public ValueTask DisposeAsync()
	{
		Dispose();

		return ValueTask.CompletedTask;
	}
}
