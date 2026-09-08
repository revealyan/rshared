using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace RShared.RabbitMq.Tests;

public class RegistrationTests
{
	private const string ConnectionString = "amqp://guest:guest@localhost:5672";

	[Fact]
	public void AddRabbitMq_requires_connection_string()
	{
		var exception = Assert.Throws<ArgumentException>(
			() => new ServiceCollection().AddRabbitMq(options => { }));

		Assert.Contains("ConnectionString is required", exception.Message);
	}

	[Fact]
	public void AddRabbitMq_registers_infrastructure_only()
	{
		var services = new ServiceCollection()
			.AddRabbitMq(options => { options.ConnectionString = ConnectionString; });

		using var provider = services.BuildServiceProvider();

		Assert.Empty(provider.GetRequiredService<RabbitMqHandlerRegistry>().Endpoints);
		Assert.Contains(services, sd => sd.ServiceType == typeof(IRabbitMqConnectionFactory));
		Assert.Contains(services, sd => sd.ServiceType == typeof(IRabbitMqPublisher));
		Assert.Contains(services, sd => sd.ServiceType == typeof(IHostedService));
	}

	[Fact]
	public void AddRabbitMqHandler_binds_queue_with_topology_overrides()
	{
		var services = new ServiceCollection()
			.AddRabbitMq(options => { options.ConnectionString = ConnectionString; })
			.AddRabbitMqHandler<ManualHandler, TestMessage>("payments", topology =>
			{
				topology.Exchange = "shop";
				topology.RoutingKey = "billing.payments";
				topology.MaxRetryCount = 5;
				topology.DeadLetterQueue = "";
			});

		using var provider = services.BuildServiceProvider();
		var endpoint = Assert.Single(provider.GetRequiredService<RabbitMqHandlerRegistry>().Endpoints);

		Assert.Equal("payments", endpoint.Queue);
		Assert.Equal("shop", endpoint.Exchange);
		Assert.Equal("billing.payments", endpoint.RoutingKey);
		Assert.Equal(5, endpoint.MaxRetryCount);
		Assert.Null(endpoint.DeadLetterQueue);
		Assert.Contains(services, sd => sd.Lifetime == ServiceLifetime.Scoped && sd.ServiceType == typeof(ManualHandler));
	}

	[Fact]
	public void AddRabbitMqHandler_defaults_dead_letter_queue()
	{
		var services = new ServiceCollection()
			.AddRabbitMq(options => { options.ConnectionString = ConnectionString; })
			.AddRabbitMqHandler<ManualHandler, TestMessage>("payments");

		using var provider = services.BuildServiceProvider();
		var endpoint = Assert.Single(provider.GetRequiredService<RabbitMqHandlerRegistry>().Endpoints);

		Assert.Equal("payments.dlq", endpoint.DeadLetterQueue);
	}

	[Fact]
	public void AddRabbitMqHandler_keeps_custom_dead_letter_queue()
	{
		var services = new ServiceCollection()
			.AddRabbitMq(options => { options.ConnectionString = ConnectionString; })
			.AddRabbitMqHandler<ManualHandler, TestMessage>("payments",
				topology => topology.DeadLetterQueue = "retry.box");

		using var provider = services.BuildServiceProvider();
		var endpoint = Assert.Single(provider.GetRequiredService<RabbitMqHandlerRegistry>().Endpoints);

		Assert.Equal("retry.box", endpoint.DeadLetterQueue);
	}

	[Fact]
	public void AddRabbitMqHandler_throws_on_duplicate_queue()
	{
		var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
			.AddRabbitMq(options => { options.ConnectionString = ConnectionString; })
			.AddRabbitMqHandler<ManualHandler, TestMessage>("orders")
			.AddRabbitMqHandler<FailingHandler, TestMessage>("orders"));

		Assert.Contains("already registered", exception.Message);
	}

	[Fact]
	public void AddRabbitMqHandler_throws_on_empty_queue_name()
	{
		var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection()
			.AddRabbitMq(options => { options.ConnectionString = ConnectionString; })
			.AddRabbitMqHandler<ManualHandler, TestMessage>(" "));

		Assert.Contains("Queue name is required", exception.Message);
	}

	[Fact]
	public void Option_ClientName_defaults_to_entry_assembly()
	{
		Assert.Equal(Assembly.GetEntryAssembly()!.GetName().Name, new RabbitMqOption().ClientName);
	}
}
