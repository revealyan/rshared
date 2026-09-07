using Microsoft.Extensions.DependencyInjection;

namespace RShared.Mediator;

/// <summary>
/// Mediator implementation
/// </summary>
internal sealed class Mediator
	: IMediator
{
	/// <summary>
	/// Scope service provider for pointwise handler resolution
	/// </summary>
	private readonly IServiceProvider _provider;

	/// <summary>
	/// Create instance of <see cref="Mediator"/>
	/// </summary>
	/// <param name="provider">Scope service provider</param>
	/// <exception cref="ArgumentNullException">Throws when provider is null</exception>
	public Mediator(IServiceProvider provider)
	{
		_provider = provider ?? throw new ArgumentNullException(nameof(provider));
	}

	/// <inheritdoc />
	public Task SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
	{
		return _provider.GetRequiredService<IMessageHandler<TMessage>>().HandleAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	public Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
	{
		return _provider.GetRequiredService<IMessageHandler<TRequest, TResponse>>().HandleAsync(request, cancellationToken);
	}
}
