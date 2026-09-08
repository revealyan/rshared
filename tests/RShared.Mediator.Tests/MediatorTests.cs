using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RShared.Mediator.Tests;

public class MediatorTests
{
	private static ServiceProvider BuildProvider()
	{
		return new ServiceCollection()
			.AddMediator(options => options.Assemblies = new[] { typeof(PingHandler).Assembly })
			.BuildServiceProvider();
	}

	[Fact]
	public async Task SendAsync_routes_message_to_handler()
	{
		using var provider = BuildProvider();
		using var scope = provider.CreateScope();

		var handler = Assert.IsType<PingHandler>(scope.ServiceProvider.GetRequiredService<IMessageHandler<Ping>>());
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		await mediator.SendAsync(new Ping());

		Assert.True(handler.Called);
	}

	[Fact]
	public async Task SendAsync_returns_response_of_handler()
	{
		using var provider = BuildProvider();
		using var scope = provider.CreateScope();

		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		var answer = await mediator.SendAsync<Checkout, string>(new Checkout());

		Assert.Equal("done", answer);
	}

	[Fact]
	public async Task SendAsync_without_handler_throws()
	{
		using var provider = BuildProvider();
		using var scope = provider.CreateScope();

		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => mediator.SendAsync(new UnknownMessage()));
	}

	public sealed record UnknownMessage;
}
