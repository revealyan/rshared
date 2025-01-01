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
		Console.WriteLine("HUI 2");

		var options = new MediatorOption();

		configure?.Invoke(options);

		if (options.AddHandlers)
		{
			var handlersTypes = (options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies())
				.SelectMany(a => a.GetTypes())
				.Where(t => t.IsClass && !t.IsAbstract && t.IsAssignableTo(MarkerType))
				.ToArray();

			foreach (var handlerType in handlersTypes)
			{
				services.TryAddMessageHandler(handlerType);
			}
		}

		return services;
	}

	/// <summary>
	/// Added message handler if it not present
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="messageHandlerType">Type of message handler</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection TryAddMessageHandler(this IServiceCollection services, Type messageHandlerType)
	{
		return services.Any(sd => sd.ImplementationType == messageHandlerType)
			? services
			: services.AddScoped(MarkerType, messageHandlerType);
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
	/// Add mediator services
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configure">Configure mediator methods</param>
	/// <returns>Service collection</returns>
	public static WebApplicationBuilder AddMediator(this WebApplicationBuilder builder, Action<MediatorOption>? configure = null)
	{
		AddMediator(builder.Services, configure);

		return builder;
	}

	/// <summary>
	/// Added message handler if it not present
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="messageHandlerType">Type of message handler</param>
	/// <returns>Service collection</returns>
	public static WebApplicationBuilder TryAddMessageHandler(this WebApplicationBuilder builder, Type messageHandlerType)
	{
		TryAddMessageHandler(builder.Services, messageHandlerType);

		return builder;
	}

	/// <summary>
	/// Added message handler if it not present
	/// </summary>
	/// <typeparam name="THandler">Type of message handler</typeparam>
	/// <param name="builder"></param>
	/// <returns>Service collection</returns>
	public static WebApplicationBuilder TryAddMessageHandler<THandler>(this WebApplicationBuilder builder)
		where THandler : class, IMessageHandler
	{
		TryAddMessageHandler(builder.Services, typeof(THandler));

		return builder;
	}
}
