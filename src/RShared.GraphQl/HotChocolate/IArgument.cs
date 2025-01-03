using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace RShared.GraphQl.HotChocolate;

public interface IArgument
{
	public void AddArgument(IObjectFieldDescriptor descriptor);
	public object? GetArgumentValue(IResolverContext context);
}
