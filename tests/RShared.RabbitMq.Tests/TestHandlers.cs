namespace RShared.RabbitMq.Tests;

public sealed record TestMessage(int OrderId);

public sealed record OtherMessage(int PaymentId);

/// <summary>
/// Ручная регистрация — атрибут не нужен
/// </summary>
public sealed class ManualHandler
	: IRabbitMqHandler<TestMessage>
{
	public static int Handled;

	public Task HandleAsync(TestMessage message, CancellationToken cancellationToken = default)
	{
		Handled++;
		return Task.CompletedTask;
	}
}

public sealed class FailingHandler
	: IRabbitMqHandler<TestMessage>
{
	public Task HandleAsync(TestMessage message, CancellationToken cancellationToken = default)
		=> throw new InvalidOperationException("boom");
}
