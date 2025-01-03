using HotChocolate;

namespace RShared.GraphQl.HotChocolate;

public interface IGraphQlErrorHandler
{
	public IError GetError(Exception exc);
}
