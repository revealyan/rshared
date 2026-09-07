using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RShared.Mediator;

/// <summary>
/// Extensions methods for dependency injection
/// </summary>
public static class MediatorExtensions
{
	/// <summary>
	/// Message handler marker type
	/// </summary>
	public readonly static Type MarkerType = typeof(IMessageHandler);

	/// <summary>
	/// Add mediator services
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configure">Configure mediator methods</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddMediator(this IServiceCollection services, Action<MediatorOption>? configure = null)
	{
		services.TryAddScoped<IMediator, Mediator>();

		var options = new MediatorOption();

		configure?.Invoke(options);

		if (options.AddHandlers)
		{
			// открытые generic-хендлеры скан не берёт — их регистрируют руками
			var handlersTypes = (options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies())
				.SelectMany(a => a.GetTypes())
				.Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && t.IsAssignableTo(MarkerType))
				.ToArray();

			foreach (var handlerType in handlersTypes)
			{
				services.TryAddMessageHandler(handlerType);
			}
		}

		return services;
	}

	/// <summary>
	/// Added message handler if it not present.
	/// Handler registers as scoped service under each closed
	/// <see cref="IMessageHandler{TMessage}"/> / <see cref="IMessageHandler{TRequest,TResponse}"/> interface
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="messageHandlerType">Type of message handler</param>
	/// <returns>Service collection</returns>
	/// <exception cref="ArgumentException">Throws when handler type is open generic</exception>
	/// <exception cref="InvalidOperationException">Throws when another handler already serves the same message</exception>
	public static IServiceCollection TryAddMessageHandler(this IServiceCollection services, Type messageHandlerType)
	{
		if (messageHandlerType.ContainsGenericParameters)
		{
			throw new ArgumentException(
				$@"Open generic handler ""{messageHandlerType.FullName}"" must be registered manually: " +
				@"services.AddScoped(typeof(IMessageHandler<>), handlerType)", nameof(messageHandlerType));
		}

		if (services.Any(sd => sd.ImplementationType == messageHandlerType))
		{
			return services;
		}

		foreach (var handlerInterface in messageHandlerType.GetInterfaces().Where(IsClosedHandlerInterface))
		{
			// сообщение уже обслуживается другим хендлером — не молчим, падаем на старте
			var registered = services.FirstOrDefault(sd => sd.ServiceType == handlerInterface);

			if (registered?.ImplementationType is { } other && other != messageHandlerType)
			{
				throw new InvalidOperationException(
					$@"Message ""{handlerInterface}"" is already served by ""{other.FullName}"", can not add ""{messageHandlerType.FullName}""");
			}

			services.TryAddScoped(handlerInterface, messageHandlerType);
		}

		return services;
	}

	/// <summary>
	/// Added message handler if it not present
	/// </summary>
	/// <typeparam name="THandler">Type of message handler</typeparam>
	/// <param name="services">Service collection</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection TryAddMessageHandler<THandler>(this IServiceCollection services)
		where THandler : class, IMessageHandler
	{
		return TryAddMessageHandler(services, typeof(THandler));
	}

	/// <summary>
	/// Check that type is closed <see cref="IMessageHandler{TMessage}"/> or <see cref="IMessageHandler{TRequest,TResponse}"/>
	/// </summary>
	private static bool IsClosedHandlerInterface(Type type)
	{
		return type.IsGenericType
			&& !type.ContainsGenericParameters
			&& type.GetGenericTypeDefinition() is { } definition
			&& (definition == typeof(IMessageHandler<>)
				|| definition == typeof(IMessageHandler<,>));
	}

	/// <summary>
	/// Add mediator services
	/// </summary>
	/// <param name="builder">Web application builder</param>
	/// <param name="configure">Configure mediator methods</param>
	/// <returns>Web application builder</returns>
	public static WebApplicationBuilder AddMediator(this WebApplicationBuilder builder, Action<MediatorOption>? configure = null)
	{
		AddMediator(builder.Services, configure);

		return builder;
	}

	/// <summary>
	/// Added message handler if it not present
	/// </summary>
	/// <param name="builder">Web application builder</param>
	/// <param name="messageHandlerType">Type of message handler</param>
	/// <returns>Web application builder</returns>
	public static WebApplicationBuilder TryAddMessageHandler(this WebApplicationBuilder builder, Type messageHandlerType)
	{
		TryAddMessageHandler(builder.Services, messageHandlerType);

		return builder;
	}

	/// <summary>
	/// Added message handler if it not present
	/// </summary>
	/// <typeparam name="THandler">Type of message handler</typeparam>
	/// <param name="builder">Web application builder</param>
	/// <returns>Web application builder</returns>
	public static WebApplicationBuilder TryAddMessageHandler<THandler>(this WebApplicationBuilder builder)
		where THandler : class, IMessageHandler
	{
		TryAddMessageHandler(builder.Services, typeof(THandler));

		return builder;
	}
}
