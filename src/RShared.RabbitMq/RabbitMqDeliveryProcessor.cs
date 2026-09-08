using System.Collections.Concurrent;

namespace RShared.RabbitMq;

/// <summary>
/// Failure policy of a delivery: ack on success, requeue while retries remain, dead-letter after.
/// Attempt counters live in memory keyed by message id — a restart starts counting anew,
/// which is safe: the cap only limits retries, it does not extend them.
/// </summary>
internal sealed class RabbitMqDeliveryProcessor
{
	private readonly ConcurrentDictionary<string, int> _failedAttempts = [];

	/// <summary>
	/// Run the handler and settle the delivery. Empty <paramref name="attemptKey"/> means the message
	/// has no id, so it cannot be retried — a failure dead-letters it right away.
	/// </summary>
	public async Task ProcessAsync(
		string attemptKey,
		int maxRetryCount,
		Func<CancellationToken, Task> handle,
		Func<CancellationToken, Task> ack,
		Func<bool, CancellationToken, Task> nack,
		CancellationToken cancellationToken)
	{
		try
		{
			await handle(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// шатдаун: не ack'аем и не nack'аем — брокер доставит сообщение заново после старта
			throw;
		}
		catch (Exception)
		{
			var attempts = _failedAttempts.AddOrUpdate(attemptKey, 1, (_, count) => count + 1);

			if (attemptKey.Length > 0 && attempts <= maxRetryCount)
			{
				await nack(true, cancellationToken).ConfigureAwait(false);
				return;
			}

			_failedAttempts.TryRemove(attemptKey, out _);
			await nack(false, cancellationToken).ConfigureAwait(false);
			return;
		}

		_failedAttempts.TryRemove(attemptKey, out _);
		await ack(cancellationToken).ConfigureAwait(false);
	}
}
