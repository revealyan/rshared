using HotChocolate.Types;
using RShared.GraphQl.HotChocolate;

namespace RShared.GraphQl.Mediator;

internal sealed class InternalResponse
	: IResponse
{
	public static InternalResponse Create()
	{
		return new InternalResponse(typeof(bool), typeof(BooleanType));
	}

	public static InternalResponse Create<TResponse, TGraphQlResponse>()
		where TGraphQlResponse : class, IOutputType
	{
		return new InternalResponse(typeof(TResponse), typeof(TGraphQlResponse));
	}

	private Type? _appType;
	private Type? _graphQlType;

	private InternalResponse(Type? appType, Type? graphQlType)
	{
		_appType = appType;
		_graphQlType = graphQlType;
	}

	public void AddGraphQlType(IObjectFieldDescriptor descriptor)
	{
		if (descriptor is null)
		{
			throw new ArgumentNullException(nameof(descriptor));
		}

		if (_graphQlType is null)
		{
			return;
		}

		descriptor.Type(_graphQlType);
	}

	public object? GetDefaultValue()
	{
		if (_appType is not null && _appType.IsValueType)
		{
			return Activator.CreateInstance(_appType);
		}

		return null;
	}

	public Type? GetApplicationType()
	{
		return _appType;
	}
}