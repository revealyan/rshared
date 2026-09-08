namespace RShared.Mediator.Tests;

public sealed record Ping;

public sealed record Checkout;

public sealed record Nothing;

/// <summary>
/// Абстрактный хендлер — скан обязан его пропускать, инстанцировать нечего
/// </summary>
public abstract class AbstractHandler
	: IMessageHandler<Nothing>
{
	public abstract Task HandleAsync(Nothing message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Не хендлер: заглушка для регистрации «чужого» обработчика на Ping
/// </summary>
public sealed class AlienHandler { }

public sealed class PingHandler
	: IMessageHandler<Ping>
{
	public bool Called;

	public Task HandleAsync(Ping message, CancellationToken cancellationToken = default)
	{
		Called = true;

		return Task.CompletedTask;
	}
}

public sealed class CheckoutHandler
	: IMessageHandler<Checkout, string>
{
	public Task<string> HandleAsync(Checkout request, CancellationToken cancellationToken = default)
	{
		return Task.FromResult("done");
	}
}

public sealed class OpenGenericHandler<TMessage>
	: IMessageHandler<TMessage>
{
	public Task HandleAsync(TMessage message, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}
