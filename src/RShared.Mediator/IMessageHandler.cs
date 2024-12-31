namespace RShared.Mediator;

/// <summary>
/// Interface marker of mediator handler
/// </summary>
public interface IMessageHandler;

/// <summary>
/// Handler of <typeparamref name="TMessage"/> message
/// </summary>
/// <typeparam name="TMessage">Message type</typeparam>
public interface IMessageHandler<in TMessage>
	: IMessageHandler
{
	public Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler of <typeparamref name="TRequest"/> request that return <typeparamref name="TResponse"/> response
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public interface IMessageHandler<in TRequest, TResponse>
	: IMessageHandler
{
	public Task<TResponse?> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}
