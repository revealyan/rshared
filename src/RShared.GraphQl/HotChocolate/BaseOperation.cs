using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace RShared.GraphQl.HotChocolate;

public abstract class BaseOperation
	: ObjectType
{
	protected readonly string SchemaPrefix;
	protected readonly List<SchemaOperationRegistrationDelegate> Registrations;

	protected BaseOperation(string schemaPrefix)
		: base()
	{
		SchemaPrefix = schemaPrefix;
		Registrations = new List<SchemaOperationRegistrationDelegate>();
	}

	protected override void Configure(IObjectTypeDescriptor descriptor)
	{
		foreach (var registration in Registrations)
		{
			registration(descriptor);
		}
	}

	protected void AddRegistration(string name, string description, IArgument[] arguments, IResponse response, OperationProcessDelegate operationProcess)
	{
		Registrations.Add((descriptor) =>
		{
			var fieldDescriptor = descriptor.Field($"{SchemaPrefix}{name}").Description(description);

			foreach (var argument in arguments)
			{
				argument.AddArgument(fieldDescriptor);
			}

			response.AddGraphQlType(fieldDescriptor);

			fieldDescriptor.Resolve(
				async context =>
				{
					try
					{
						return await operationProcess(context.Services, arguments.Select(a => a.GetArgumentValue(context)).ToArray(), response.GetApplicationType(), context.RequestAborted);
					}
					catch (Exception exc)
					{
						var errorHandler = context.Services.GetService<IGraphQlErrorHandler>();
						if (errorHandler is not null)
						{
							context.ReportError(errorHandler.GetError(exc));
						}
						else
						{
#if DEBUG
							context.ReportError(exc.ToString());
#endif

#if REALESE
							context.ReportError(exc.Message);
#endif
						}
						return response.GetDefaultValue();
					}
				});


		});
	}
}

