using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using RShared.GraphQl.HotChocolate;
using RShared.Mediator;
using System.Reflection;

namespace RShared.GraphQl.Mediator;

public abstract class BaseQuery
	: BaseOperation
{
	protected readonly string ArgumentName;
	protected readonly MethodInfo SendAsyncMethod;

	protected BaseQuery(string schemaPrefix, string argumentName)
		: base(schemaPrefix)
	{
		ArgumentName = argumentName;

		var methods = typeof(IMediator).GetMethods(BindingFlags.Public | BindingFlags.Instance);

		SendAsyncMethod = methods.Where(m => m.Name == "SendAsync" && m.GetGenericArguments().Length == 2).Single();
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

		var sendAsync = SendAsyncMethod.MakeGenericMethod(requestType, responseType);

		var task = sendAsync.Invoke(mediator, [request, cancellationToken]) as Task;

		if (task is null)
		{
			throw new InvalidOperationException("IMediator.SendAsync must be async");
		}

		await task.ConfigureAwait(false);


		var resultProperty = task.GetType().GetProperty("Result");


		if (resultProperty is null)
		{
			throw new InvalidOperationException("IMediator.SendAsync must be async and return value");
		}

		return resultProperty.GetValue(task);
	}

	protected override void Configure(IObjectTypeDescriptor descriptor)
	{
		descriptor.Name(OperationTypeNames.Query);

		base.Configure(descriptor);
	}


	protected void AddQuery<TRequest, TGraphQlRequest, TResponse, TGraphQlResponse>(string description)
		where TRequest : new()
		where TGraphQlRequest : class, IInputType
		where TGraphQlResponse : class, IOutputType
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest, TGraphQlRequest>(ArgumentName)],
			InternalResponse.Create<TResponse, TGraphQlResponse>(), ProcessAsync);
	}

	protected void AddQueryReturningCollection<TRequest, TGraphQlRequest, TResponse, TGraphQlResponse>(string description)
		where TRequest : new()
		where TGraphQlRequest : class, IInputType
		where TGraphQlResponse : class, IOutputType
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest, TGraphQlRequest>(ArgumentName)],
			InternalResponse.Create<List<TResponse>, NonNullType<ListType<TGraphQlResponse>>>(), ProcessAsync);
	}

	protected void AddQueryWithNoRequest<TRequest, TResponse, TGraphQlResponse>(string description)
		where TRequest : new()
		where TGraphQlResponse : class, IOutputType
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest>()],
			InternalResponse.Create<TResponse, TGraphQlResponse>(), ProcessAsync);
	}


	protected void AddQueryWithNoRequestReturningCollection<TRequest, TResponse, TGraphQlResponse>(string description)
		where TRequest : new()
		where TGraphQlResponse : class, IOutputType
	{
		AddRegistration(typeof(TRequest).Name, description, [InternalArgument.Create<TRequest>()],
			InternalResponse.Create<List<TResponse>, NonNullType<ListType<TGraphQlResponse>>>(), ProcessAsync);
	}

}

