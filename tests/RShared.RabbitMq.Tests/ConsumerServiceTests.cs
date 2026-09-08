using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace RShared.RabbitMq.Tests;

public class ConsumerServiceTests
{
	private const string ConnectionString = "amqp://guest:guest@localhost:5672";

	[Fact]
	public async Task StartAsync_declares_dead_letter_topology()
	{
		var (_, _, connection, channel) = await BuildServiceAsync();

		await channel.Received(1).QueueDeclareAsync("orders.dlq", true, false, false,
			cancellationToken: Arg.Any<CancellationToken>());
		await channel.Received(1).QueueDeclareAsync("orders", true, false, false,
			Arg.Is<IDictionary<string, object?>?>(arguments => arguments != null
				&& (string?)arguments["x-dead-letter-exchange"] == string.Empty
				&& (string?)arguments["x-dead-letter-routing-key"] == "orders.dlq"),
			cancellationToken: Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task StartAsync_without_dead_letter_queue_skips_dlq()
	{
		var (_, _, connection, channel) = await BuildServiceAsync(topology => topology.DeadLetterQueue = "");

		await channel.Received(1).QueueDeclareAsync("orders", true, false, false,
			Arg.Is<IDictionary<string, object?>?>(arguments => arguments == null || arguments.Count == 0),
			cancellationToken: Arg.Any<CancellationToken>());
		await channel.DidNotReceive().QueueDeclareAsync("orders.dlq", Arg.Any<bool>(), Arg.Any<bool>(),
			Arg.Any<bool>(), cancellationToken: Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task StartAsync_without_exchange_skips_binding()
	{
		var (_, _, _, channel) = await BuildServiceAsync();

		await channel.DidNotReceiveWithAnyArgs().ExchangeDeclareAsync(default, default, default, default);
		await channel.DidNotReceiveWithAnyArgs().QueueBindAsync(default, default, default);
	}

	[Fact]
	public async Task StartAsync_binds_exchange()
	{
		var (_, _, _, channel) = await BuildServiceAsync(topology => topology.Exchange = "shop");

		await channel.Received(1).ExchangeDeclareAsync("shop", ExchangeType.Direct, true,
			cancellationToken: Arg.Any<CancellationToken>());
		await channel.Received(1).QueueBindAsync("orders", "shop", "orders",
			cancellationToken: Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task StartAsync_sets_prefetch()
	{
		var (_, _, _, channel) = await BuildServiceAsync(optionSetup: option => option.PrefetchCount = 32);

		await channel.Received(1).BasicQosAsync(0, 32, false, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task StartAsync_consumes_queue_with_manual_acks()
	{
		var (_, _, _, channel) = await BuildServiceAsync();

		await channel.Received(1).BasicConsumeAsync("orders", false,
			Arg.Any<IAsyncBasicConsumer>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task StartAsync_without_handlers_does_not_connect()
	{
		var factory = Substitute.For<IRabbitMqConnectionFactory>();
		var logger = new TestLogger<RabbitMqConsumerService>();
		var services = new ServiceCollection()
			.AddRabbitMq(options => { options.ConnectionString = ConnectionString; });
		services.RemoveAll<IRabbitMqConnectionFactory>();
		services.AddSingleton(factory);
		services.AddSingleton<ILogger<RabbitMqConsumerService>>(logger);

		using var provider = services.BuildServiceProvider();

		await provider.GetRequiredService<IHostedService>().StartAsync(CancellationToken.None);

		await factory.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
		Assert.Contains(logger.Entries, entry => entry.Message.Contains("No RabbitMq handlers registered"));
	}

	[Fact]
	public async Task StartAsync_logs_consumer_start()
	{
		var logger = new TestLogger<RabbitMqConsumerService>();
		await BuildServiceAsync(logger: logger);

		Assert.Contains(logger.Entries, entry => entry.Message.Contains("Starting RabbitMq consumers for 1 queue(s)"));
	}

	[Fact]
	public async Task Delivery_event_dispatches_to_handler()
	{
		ManualHandler.Handled = 0;
		var factory = Substitute.For<IRabbitMqConnectionFactory>();
		var connection = Substitute.For<IConnection>();
		var channel = Substitute.For<IChannel>();
		IAsyncBasicConsumer? consumer = null;

		channel.IsOpen.Returns(true);
		connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
			.Returns(channel);
		factory.CreateAsync(Arg.Any<RabbitMqOption>(), Arg.Any<CancellationToken>())
			.Returns(connection);

		// захват консюмера при вызове BasicConsumeAsync во время старта;
		// 4-аргументная версия — extension-метод, он путает спеки NSubstitute, поэтому полный профиль инстанса
		await channel.BasicConsumeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object?>?>(),
			Arg.Do<IAsyncBasicConsumer>(subscribed => consumer = subscribed), Arg.Any<CancellationToken>());

		var services = new ServiceCollection()
			.AddRabbitMq(options => { options.ConnectionString = ConnectionString; })
			.AddRabbitMqHandler<ManualHandler, TestMessage>("orders");
		services.RemoveAll<IRabbitMqConnectionFactory>();
		services.AddSingleton(factory);

		using var provider = services.BuildServiceProvider();

		await provider.GetRequiredService<IHostedService>().StartAsync(CancellationToken.None);

		// брокер доставил сообщение — событие должно дойти до хендлера и завершиться ack'ом
		var eventing = Assert.IsType<AsyncEventingBasicConsumer>(consumer);
		await eventing.HandleBasicDeliverAsync("tests", 1, false, string.Empty, "orders",
			new BasicProperties { MessageId = "m1" }, Serialize(new TestMessage(7)), CancellationToken.None);

		Assert.Equal(1, ManualHandler.Handled);
		await channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Dispatch_success_invokes_scoped_handler_and_acks()
	{
		var (service, provider, _, channel) = await BuildServiceAsync();
		ManualHandler.Handled = 0;
		var endpoint = provider.GetRequiredService<RabbitMqHandlerRegistry>().Endpoints.Single();

		await service.DispatchAsync(endpoint, channel, Deliver(Serialize(new TestMessage(7)), "m1"));

		Assert.Equal(1, ManualHandler.Handled);
		await channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Dispatch_poison_body_rejects_without_handler()
	{
		var (service, provider, _, channel) = await BuildServiceAsync();
		ManualHandler.Handled = 0;
		var endpoint = provider.GetRequiredService<RabbitMqHandlerRegistry>().Endpoints.Single();

		await service.DispatchAsync(endpoint, channel, Deliver("\"not-json"u8.ToArray(), "m1"));

		Assert.Equal(0, ManualHandler.Handled);
		await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Dispatch_failure_requeues_then_dead_letters()
	{
		var (service, provider, _, channel) = await BuildServiceAsync(
			handlerSelector: services => services.AddRabbitMqHandler<FailingHandler, TestMessage>("orders",
				topology => topology.MaxRetryCount = 1));
		var endpoint = provider.GetRequiredService<RabbitMqHandlerRegistry>().Endpoints.Single();

		// первая неудача — перепоставка
		await service.DispatchAsync(endpoint, channel, Deliver(Serialize(new TestMessage(7)), "m1"));

		await channel.Received(1).BasicNackAsync(1, false, true, Arg.Any<CancellationToken>());

		// вторая неудача того же сообщения — в dead-letter
		await service.DispatchAsync(endpoint, channel, Deliver(Serialize(new TestMessage(7)), "m1"));

		await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task StopAsync_closes_and_disposes_channels_and_connection()
	{
		var (service, _, connection, channel) = await BuildServiceAsync();

		await service.StopAsync();
		// повторный стоп не должен закрывать уже закрытое
		await service.StopAsync();

		await channel.Received(1).CloseAsync(Arg.Any<CancellationToken>());
		await channel.Received(1).DisposeAsync();
		await connection.Received(1).CloseAsync(Arg.Any<CancellationToken>());
		await connection.Received(1).DisposeAsync();
	}

	private static byte[] Serialize(TestMessage message)
	{
		return JsonSerializer.SerializeToUtf8Bytes(message);
	}

	/// <summary>
	/// В 7.x поля доставки readonly — собираем через конструктор
	/// </summary>
	private static BasicDeliverEventArgs Deliver(byte[] body, string messageId)
	{
		return new BasicDeliverEventArgs("tests", 1, false, string.Empty, "orders",
			new BasicProperties { MessageId = messageId }, new ReadOnlyMemory<byte>(body), CancellationToken.None);
	}

	private static async Task<(RabbitMqConsumerService Service, ServiceProvider Provider,
		IConnection Connection, IChannel Channel)> BuildServiceAsync(
		Action<RabbitMqTopology>? topology = null,
		Action<RabbitMqOption>? optionSetup = null,
		Func<IServiceCollection, IServiceCollection>? handlerSelector = null,
		ILogger<RabbitMqConsumerService>? logger = null)
	{
		var factory = Substitute.For<IRabbitMqConnectionFactory>();
		var connection = Substitute.For<IConnection>();
		var channel = Substitute.For<IChannel>();

		connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
			.Returns(channel);
		factory.CreateAsync(Arg.Any<RabbitMqOption>(), Arg.Any<CancellationToken>())
			.Returns(connection);

		var services = new ServiceCollection()
			.AddRabbitMq(options =>
			{
				options.ConnectionString = ConnectionString;
				optionSetup?.Invoke(options);
			});

		(handlerSelector ?? (s => s.AddRabbitMqHandler<ManualHandler, TestMessage>("orders", topology)))(services);

		services.RemoveAll<IRabbitMqConnectionFactory>();
		services.AddSingleton(factory);

		if (logger is not null)
		{
			services.AddSingleton<ILogger<RabbitMqConsumerService>>(logger);
		}

		var provider = services.BuildServiceProvider();
		var service = (RabbitMqConsumerService)provider.GetRequiredService<IHostedService>();

		await service.StartAsync(CancellationToken.None);

		return (service, provider, connection, channel);
	}
}
