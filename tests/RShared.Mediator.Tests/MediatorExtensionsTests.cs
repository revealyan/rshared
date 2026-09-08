using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RShared.Mediator.Tests;

public class MediatorExtensionsTests
{
	[Fact]
	public void AddMediator_scans_assembly_for_closed_handlers()
	{
		var services = new ServiceCollection()
			.AddMediator(options => options.Assemblies = new[] { typeof(PingHandler).Assembly });

		Assert.Contains(services, sd =>
			sd.ServiceType == typeof(IMessageHandler<Ping>) && sd.ImplementationType == typeof(PingHandler));
		Assert.Contains(services, sd =>
			sd.ServiceType == typeof(IMessageHandler<Checkout, string>) && sd.ImplementationType == typeof(CheckoutHandler));
	}

	[Fact]
	public void AddMediator_skips_open_generic_handlers()
	{
		var services = new ServiceCollection()
			.AddMediator(options => options.Assemblies = new[] { typeof(OpenGenericHandler<>).Assembly });

		Assert.DoesNotContain(services, sd => sd.ServiceType == typeof(IMessageHandler<>));
	}

	[Fact]
	public void AddMediator_without_handlers_registers_only_mediator()
	{
		var services = new ServiceCollection()
			.AddMediator(options =>
			{
				options.AddHandlers = false;
				options.Assemblies = new[] { typeof(PingHandler).Assembly };
			});

		Assert.DoesNotContain(services, sd => sd.ServiceType == typeof(IMessageHandler<Ping>));
		Assert.Contains(services, sd => sd.ServiceType == typeof(IMediator));
	}

	[Fact]
	public void AddMediator_skips_abstract_handlers()
	{
		var services = new ServiceCollection()
			.AddMediator(options => options.Assemblies = new[] { typeof(PingHandler).Assembly });

		Assert.DoesNotContain(services, sd => sd.ServiceType == typeof(IMessageHandler<Nothing>));
	}

	[Fact]
	public void TryAddMessageHandler_throws_when_message_already_served()
	{
		var services = new ServiceCollection()
			.AddScoped(typeof(IMessageHandler<Ping>), typeof(AlienHandler));

		var exception = Assert.Throws<InvalidOperationException>(
			() => services.TryAddMessageHandler(typeof(PingHandler)));

		Assert.Contains("already served by", exception.Message);
	}

	[Fact]
	public void TryAddMessageHandler_throws_on_open_generic()
	{
		var services = new ServiceCollection();

		var exception = Assert.Throws<ArgumentException>(
			() => services.TryAddMessageHandler(typeof(OpenGenericHandler<>)));

		Assert.Contains("must be registered manually", exception.Message);
		Assert.Contains("IMessageHandler<>", exception.Message);
	}

	[Fact]
	public void TryAddMessageHandler_skips_same_handler_twice()
	{
		var services = new ServiceCollection();

		services.TryAddMessageHandler(typeof(PingHandler));
		services.TryAddMessageHandler(typeof(PingHandler));

		Assert.Single(services, sd => sd.ServiceType == typeof(IMessageHandler<Ping>));
	}
}
