namespace RShared.Mediator;

/// <summary>
/// Mediator interface
/// </summary>
public interface IMediator
{
	/// <summary>
	/// Async sending message to handler
	/// </summary>
	/// <typeparam name="TMessage">Message type</typeparam>
	/// <param name="message">Message object</param>
	/// <param name="cancellationToken">Operation cancellation token</param>
	/// <returns>Task that represeting message handling</returns>
	public Task SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default);

	/// <summary>
	/// Async sending request to handler and wait for response
	/// </summary>
	/// <typeparam name="TRequest">Request type</typeparam>
	/// <typeparam name="TResponse">Response type</typeparam>
	/// <param name="request">Request object</param>
	/// <param name="cancellationToken">Operation cancellation token</param>
	/// <returns>Response of request handling</returns>
	public Task<TResponse?> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default);
}
