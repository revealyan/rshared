
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq extensions methods for dependency injection
/// </summary>
public static class RabbitMqExtensions
{
	/// <summary>
	/// Message processor marker type
	/// </summary>
	public static readonly Type MessageProcessorMarker = typeof(IRabbitMqMessageProcessor);

	/// <summary>
	/// Add RabbitMq services
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configuration">Configuration</param>
	/// <param name="configure">Configuration method</param>
	/// <returns>Service collection</returns>
	public static IApplicationBuilder UseRabbitMq(this IApplicationBuilder app)
	{
		var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
		var adapter = app.ApplicationServices.GetRequiredService<IRabbitMqConsumerAdapter>();

		lifetime.ApplicationStarted.Register(() =>
		{
			adapter.StartAsync().Wait();
		});

		lifetime.ApplicationStopping.Register(() =>
		{
			adapter.StopAsync().Wait();
		});

		return app;
	}


	/// <summary>
	/// Add RabbitMq services
	/// </summary>
	/// <param name="builder">Application builder</param>
	/// <param name="configure">Configuration method</param>
	/// <returns>Service collection</returns>
	public static WebApplicationBuilder AddRabbitMq(this WebApplicationBuilder builder, Action<RabbitMqOption>? configure = null)
	{
		AddRabbitMq(builder.Services, builder.Configuration, configure);

		return builder;
	}

	/// <summary>
	/// Add RabbitMq services
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configuration">Configuration</param>
	/// <param name="configure">Configuration method</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration, Action<RabbitMqOption>? configure = null)
	{
		var configurations = configuration.GetSection("rabbitmq")?.Get<RabbitMqConfiguration[]>() ?? throw new ArgumentNullException("RabbitMq configurations not found");

		services.TryAddSingleton(sp => new RabbitMqAdapter(configurations,
				sp.GetServices<IRabbitMqMessageProcessor>(),
				sp.GetServices<IRabbitMqMessageSerializer>(),
				sp.GetService<IDefaultRabbitMqMessageSerializer>()));
		services.TryAddSingleton<IRabbitMqConsumerAdapter>(sp => sp.GetRequiredService<RabbitMqAdapter>());
		services.TryAddSingleton<IRabbitMqPublisherAdapter>(sp => sp.GetRequiredService<RabbitMqAdapter>());

		var options = new RabbitMqOption();

		configure?.Invoke(options);

		if (options.AddAllProcessors)
		{
			var processorTypes = (options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies())
				.SelectMany(a => a.GetTypes())
				.Where(t => t.IsClass && !t.IsAbstract && t.IsAssignableTo(MessageProcessorMarker))
				.ToArray();

			foreach (var processorType in processorTypes)
			{
				services.TryAddRabbitMqProcessor(processorType);
			}
		}

		if (options.UseDefaultSerializer)
		{
			services.TryAddSingleton<IDefaultRabbitMqMessageSerializer>(new DefaultJsonRabbitMqMessageSerializer(options.JsonSerializerOptions));
		}

		return services;
	}

	/// <summary>
	/// Add RabbitMq processor if it's not present in service collection
	/// </summary>
	/// <param name="builder">Web application builder</param>
	/// <param name="messageProcessorType">Message processor type</param>
	/// <returns>Service collection</returns>
	public static WebApplicationBuilder TryAddRabbitMqProcessor(this WebApplicationBuilder builder, Type messageProcessorType)
	{
		TryAddRabbitMqProcessor(builder.Services, messageProcessorType);

		return builder;
	}

	/// <summary>
	/// Add RabbitMq processor if it's not present in service collection
	/// </summary>
	/// <typeparam name="TProcessor">Message processor type</typeparam>
	/// <param name="builder">Web application builder</param>
	/// <returns>Service collection</returns>
	public static WebApplicationBuilder TryAddRabbitMqProcessor<TProcessor>(this WebApplicationBuilder builder)
		where TProcessor : class, IRabbitMqMessageProcessor
	{
		TryAddRabbitMqProcessor(builder.Services, typeof(TProcessor));

		return builder;
	}

	/// <summary>
	/// Add RabbitMq processor if it's not present in service collection
	/// </summary>
	/// <typeparam name="TProcessor">Message processor type</typeparam>
	/// <param name="services">Service collection</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection TryAddRabbitMqProcessor<TProcessor>(this IServiceCollection services)
		where TProcessor : class, IRabbitMqMessageProcessor
	{
		return TryAddRabbitMqProcessor(services, typeof(TProcessor));
	}

	/// <summary>
	/// Add RabbitMq processor if it's not present in service collection
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="messageProcessorType">Message processor type</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection TryAddRabbitMqProcessor(this IServiceCollection services, Type messageProcessorType)
	{
		return services.Any(sd => sd.ImplementationType == messageProcessorType)
			? services
			: services.AddSingleton(MessageProcessorMarker, messageProcessorType);
	}
}
