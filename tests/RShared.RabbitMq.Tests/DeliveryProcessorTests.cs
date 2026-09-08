using Xunit;

namespace RShared.RabbitMq.Tests;

public class DeliveryProcessorTests
{
	[Fact]
	public async Task Success_acks_once()
	{
		var processor = new RabbitMqDeliveryProcessor();
		var acks = 0;
		var nacks = new List<bool>();

		await processor.ProcessAsync("orders:m1", 3,
			_ => Task.CompletedTask,
			_ => { acks++; return Task.CompletedTask; },
			(requeue, _) => { nacks.Add(requeue); return Task.CompletedTask; },
			CancellationToken.None);

		Assert.Equal(1, acks);
		Assert.Empty(nacks);
	}

	[Fact]
	public async Task Failure_requeues_up_to_max_then_rejects()
	{
		var processor = new RabbitMqDeliveryProcessor();
		var nacks = new List<bool>();
		var acks = 0;

		for (var i = 0; i < 4; i++)
		{
			await processor.ProcessAsync("orders:m1", 3,
				_ => throw new InvalidOperationException("boom"),
				_ => { acks++; return Task.CompletedTask; },
				(requeue, _) => { nacks.Add(requeue); return Task.CompletedTask; },
				CancellationToken.None);
		}

		// MaxRetryCount = 3: первые три неудачи перепоставляются, четвёртая — в dead-letter
		Assert.Equal([true, true, true, false], nacks);
		// отклонённое сообщение не подтверждается
		Assert.Equal(0, acks);
	}

	[Fact]
	public async Task Success_resets_attempt_counter()
	{
		var processor = new RabbitMqDeliveryProcessor();
		var nacks = new List<bool>();

		// неудача (попытка 1 из 1) → requeue
		await processor.ProcessAsync("orders:m1", 1,
			_ => throw new InvalidOperationException("boom"),
			_ => Task.CompletedTask,
			(requeue, _) => { nacks.Add(requeue); return Task.CompletedTask; },
			CancellationToken.None);

		// успех между неудачами сбрасывает счётчик
		await processor.ProcessAsync("orders:m1", 1,
			_ => Task.CompletedTask,
			_ => Task.CompletedTask,
			(requeue, _) => { nacks.Add(requeue); return Task.CompletedTask; },
			CancellationToken.None);

		// снова неудача → счётчик с нуля, requeue, а не reject
		await processor.ProcessAsync("orders:m1", 1,
			_ => throw new InvalidOperationException("boom"),
			_ => Task.CompletedTask,
			(requeue, _) => { nacks.Add(requeue); return Task.CompletedTask; },
			CancellationToken.None);

		Assert.Equal([true, true], nacks);
	}

	[Fact]
	public async Task Failure_resets_counter_after_reject()
	{
		var processor = new RabbitMqDeliveryProcessor();
		var nacks = new List<bool>();

		for (var i = 0; i < 3; i++)
		{
			await processor.ProcessAsync("orders:m1", 1,
				_ => throw new InvalidOperationException("boom"),
				_ => Task.CompletedTask,
				(requeue, _) => { nacks.Add(requeue); return Task.CompletedTask; },
				CancellationToken.None);
		}

		// max=1: перепоставка, dead-letter, и снова перепоставка — счётчик сброшен после reject
		// (например, сообщение вернулось из DLQ)
		Assert.Equal([true, false, true], nacks);
	}

	[Fact]
	public async Task Failure_without_message_id_rejects_immediately()
	{
		var processor = new RabbitMqDeliveryProcessor();
		var nacks = new List<bool>();

		// у сообщения нет id — ретраи не считаем, сразу dead-letter
		await processor.ProcessAsync(string.Empty, 3,
			_ => throw new InvalidOperationException("boom"),
			_ => Task.CompletedTask,
			(requeue, _) => { nacks.Add(requeue); return Task.CompletedTask; },
			CancellationToken.None);

		Assert.Equal([false], nacks);
	}

	[Fact]
	public async Task Shutdown_cancellation_rethrows_without_settling()
	{
		var processor = new RabbitMqDeliveryProcessor();
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() => processor.ProcessAsync("orders:m1", 3,
			_ => throw new OperationCanceledException(cancellation.Token),
			_ => throw new Xunit.Sdk.XunitException("не должен ack'ать при шатдауне"),
			(_, _) => throw new Xunit.Sdk.XunitException("не должен nack'ать при шатдауне"),
			cancellation.Token));
	}
}
