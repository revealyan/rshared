using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RShared.GraphQl.HotChocolate;

public static class GraphQlHotChocolateExtensions
{
	public static IServiceCollection TryAddGraphQlErrorHandler<THandler>(this IServiceCollection services)
		where THandler : class, IGraphQlErrorHandler
	{
		services.TryAddScoped<IGraphQlErrorHandler, THandler>();

		return services;
	}

	public static IServiceCollection AddDefaultGraphQlErrorHandler(this IServiceCollection services)
	{
		services.TryAddScoped<IGraphQlErrorHandler, GraphQlErrorHandler>();

		return services;
	}

	public static WebApplicationBuilder TryAddGraphQlErrorHandler<THandler>(this WebApplicationBuilder builder)
		where THandler : class, IGraphQlErrorHandler
	{
		TryAddGraphQlErrorHandler<THandler>(builder.Services);

		return builder;
	}

	public static WebApplicationBuilder AddDefaultGraphQlErrorHandler(this WebApplicationBuilder builder)
	{
		AddDefaultGraphQlErrorHandler(builder.Services);

		return builder;
	}
}
