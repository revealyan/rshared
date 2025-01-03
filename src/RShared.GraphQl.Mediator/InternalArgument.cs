using HotChocolate.Resolvers;
using HotChocolate.Types;
using RShared.GraphQl.HotChocolate;

namespace RShared.GraphQl.Mediator;

internal sealed class InternalArgument
	: IArgument
{
	public static InternalArgument Create<TRequest>()
		where TRequest : new()
	{
		return new InternalArgument(string.Empty, typeof(TRequest), null);
	}

	public static InternalArgument Create<TRequest, TGraphQlRequest>(string argName)
		where TRequest : new()
		where TGraphQlRequest : class, IInputType
	{
		return new InternalArgument(argName, typeof(TRequest), typeof(TGraphQlRequest));
	}


	private readonly string _name;
	private readonly Type? _appType;
	private readonly Type? _graphQlType;

	private InternalArgument(string name, Type? appType, Type? graphQlType)
	{
		_name = name;
		_appType = appType;
		_graphQlType = graphQlType;
	}

	public void AddArgument(IObjectFieldDescriptor descriptor)
	{
		if (descriptor is null)
		{
			throw new ArgumentNullException(nameof(descriptor));
		}

		if (_graphQlType is null)
		{
			return;
		}

		descriptor.Argument(_name, a => a.Type(_graphQlType));
	}

	public object? GetArgumentValue(IResolverContext context)
	{
		if (context is null)
		{
			throw new ArgumentNullException(nameof(context));
		}

		if (_appType is null)
		{
			return null;
		}

		var method = context.GetType().GetMethod(nameof(context.ArgumentValue))
			?? throw new InvalidOperationException("Can't find method ArgumentValue<T>");

		return method.MakeGenericMethod(_appType).Invoke(context, [_name]);
	}
}