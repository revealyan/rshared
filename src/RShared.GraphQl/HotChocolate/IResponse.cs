using HotChocolate.Types;

namespace RShared.GraphQl.HotChocolate;

public interface IResponse
{
	public void AddGraphQlType(IObjectFieldDescriptor descriptor);
	public Type? GetApplicationType();
	public object? GetDefaultValue();
}
