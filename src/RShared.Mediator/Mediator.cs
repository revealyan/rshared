namespace RShared.Mediator;

/// <summary>
/// Mediator implementation
/// </summary>
internal sealed class Mediator
		: IMediator
{
	/// <summary>
	/// Message handlers
	/// </summary>
	private readonly IMessageHandler[] _handlers;

	/// <summary>
	/// Create instance of <see cref="Mediator"/>
	/// </summary>
	/// <param name="handlers">Collection of message handlers</param>
	/// <exception cref="ArgumentNullException">Throws when handlers is null</exception>
	public Mediator(IEnumerable<IMessageHandler> handlers)
	{
		_handlers = handlers?.ToArray() ?? throw new ArgumentNullException(nameof(handlers));
	}

	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">Throws when can not find single instanse handler</exception>
	public Task SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
	{
		return GetHandler<IMessageHandler<TMessage>>().HandleAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">Throws when can not find single instanse handler</exception>
	public Task<TResponse?> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
	{
		return GetHandler<IMessageHandler<TRequest, TResponse>>().HandleAsync(request, cancellationToken);
	}

	/// <summary>
	/// Get handler of <typeparamref name="THandler"/> or throws
	/// </summary>
	/// <typeparam name="THandler"></typeparam>
	/// <returns>Instanse of <typeparamref name="THandler"/> handler</returns>
	/// <exception cref="InvalidOperationException">Throws when can not find single instanse handler</exception>
	private THandler GetHandler<THandler>()
	{
		try
		{
			return _handlers.OfType<THandler>().Single();
		}
		catch (InvalidOperationException)
		{
			throw new InvalidOperationException($@"Could not find a single instance handler type of ""{typeof(THandler)}""");
		}
	}
}
