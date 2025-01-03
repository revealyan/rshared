using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using RShared.GraphQl.Mediator;
using RShared.Mediator;
using System.Reflection;

namespace RShared.GraphQl.HotChocolate;

public abstract class BaseMutation
	: BaseOperation
{
	protected readonly string ArgumentName;
	protected readonly MethodInfo SendAsyncMethod;
	protected readonly MethodInfo SendAsyncWithReturningValueMethod;

	protected BaseMutation(string schemaPrefix, string argumentName)
		: base(schemaPrefix)
	{
		ArgumentName = argumentName;

		var methods = typeof(IMediator).GetMethods(BindingFlags.Public | BindingFlags.Instance);
		SendAsyncMethod = methods.Where(m => m.Name == "SendAsync" && m.GetGenericArguments().Length == 1).Single();
		SendAsyncWithReturningValueMethod = methods.Where(m => m.Name == "SendAsync" && m.GetGenericArguments().Length == 2).Single();
	}

	protected override void Configure(IObjectTypeDescriptor descriptor)
	{
		descriptor.Name(OperationTypeNames.Mutation);

		base.Configure(descriptor);
	}

	protected virtual async Task<object?> ProcessAsync(IServiceProvider serviceProvider, object?[] @params, Type? responseType, CancellationToken cancellationToken = default)
	{
		var mediator = serviceProvider.GetRequiredService<IMediator>();

		var request = @params.SingleOrDefault() ?? throw new ArgumentNullException(nameof(@params));

		var requestType = request.GetType();

		if (responseType is null)
		{
			throw new ArgumentNullException(nameof(responseType));
		}

		var sendAsync = SendAsyncWithReturningValueMethod;

		var noReturningValue = responseType == typeof(bool);

		if (noReturningValue)
		{
			sendAsync = SendAsyncMethod.MakeGenericMethod(requestType);
		}
		else
		{
			sendAsync = SendAsyncWithReturningValueMethod.MakeGenericMethod(requestType, responseType);
		}


		var task = sendAsync.Invoke(mediator, [request, cancellationToken]) as Task;

		if (task is null)
		{
			throw new InvalidOperationException("IMediator.SendAsync must be async");
		}

		await task.ConfigureAwait(false);

		if (noReturningValue)
		{
			return true;
		}


		var resultProperty = task.GetType().GetProperty("Result");


		if (resultProperty is null)
		{
			throw new InvalidOperationException("IMediator.SendAsync must be async and return value");
		}

		return resultProperty.GetValue(task);
	}


	protected void AddMutation<TRequest, TGraphQlRequest>(string description)
		where TRequest : new()
		where TGraphQlRequest : class, IInputType
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest, TGraphQlRequest>(ArgumentName)],
			InternalResponse.Create(), ProcessAsync);
	}


	protected void AddMutationWithNoRequest<TRequest>(string description)
		where TRequest : new()
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest>()], InternalResponse.Create(), ProcessAsync);
	}

	protected void AddMutationWithReturningValue<TRequest, TGraphQlRequest, TResponse, TGraphQlResponse>(string description)
		where TRequest : new()
		where TGraphQlRequest : class, IInputType
		where TGraphQlResponse : class, IOutputType
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest, TGraphQlRequest>(ArgumentName)],
			InternalResponse.Create<TResponse, TGraphQlResponse>(), ProcessAsync);
	}

	protected void AddMutationWithNoRequestWithReturningValue<TRequest, TResponse, TGraphQlResponse>(string description)
		where TRequest : new()
		where TGraphQlResponse : class, IOutputType
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest>()], InternalResponse.Create<TResponse, TGraphQlResponse>(), ProcessAsync);
	}

	protected void AddMutationWithReturningCollection<TRequest, TGraphQlRequest, TResponse, TGraphQlResponse>(string description)
		where TRequest : new()
		where TGraphQlRequest : class, IInputType
		where TGraphQlResponse : class, IOutputType
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest, TGraphQlRequest>(ArgumentName)],
			InternalResponse.Create<TResponse, TGraphQlResponse>(), ProcessAsync);
	}

	protected void AddMutationWithNoRequestWithReturningCollection<TRequest, TResponse, TGraphQlResponse>(string description)
		where TRequest : new()
		where TGraphQlResponse : class, IOutputType
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest>()], InternalResponse.Create<TResponse, TGraphQlResponse>(), ProcessAsync);
	}
}

