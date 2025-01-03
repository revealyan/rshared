using HotChocolate;
using Microsoft.Extensions.Logging;

namespace RShared.GraphQl.HotChocolate;

internal sealed class GraphQlErrorHandler
	: IGraphQlErrorHandler
{
	private readonly ILogger<IGraphQlErrorHandler> _logger;

	public GraphQlErrorHandler(ILogger<IGraphQlErrorHandler> logger)
	{
		_logger = logger;
	}

	public IError GetError(Exception exc)
	{
		_logger.LogError(exc, "GraphQl error handled");
		return new SimpleGraphQlError(exc);
	}
}
