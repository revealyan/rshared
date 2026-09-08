using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;
using Xunit;

namespace RShared.RabbitMq.Tests;

public class PublisherTests
{
	[Fact]
	public async Task Publish_routes_to_default_exchange_with_queue_as_key()
	{
		var (publisher, _, _, channel) = BuildPublisher();
		using var cancellation = new CancellationTokenSource();

		await publisher.PublishAsync("orders", new TestMessage(7), cancellation.Token);

		await channel.Received(1).BasicPublishAsync(string.Empty, "orders", false,
			Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(), cancellation.Token);
	}

	[Fact]
	public async Task Publish_sets_identifying_properties_and_serializes_body()
	{
		var (publisher, _, _, channel) = BuildPublisher();

		await publisher.PublishAsync("orders", new TestMessage(7));

		await channel.Received(1).BasicPublishAsync(string.Empty, "orders", false,
			Arg.Is<BasicProperties>(p => p.MessageId.Length == 32
				&& p.Type == nameof(TestMessage)
				&& p.ContentType == "application/json"
				&& p.DeliveryMode == DeliveryModes.Persistent),
			Arg.Is<ReadOnlyMemory<byte>>(b => Encoding.UTF8.GetString(b.ToArray()).Contains("\"OrderId\":7")),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Publish_without_persistence_uses_transient_mode()
	{
		var (publisher, _, _, channel) = BuildPublisher(options => options.PersistentMessages = false);

		await publisher.PublishAsync("orders", new TestMessage(7));

		await channel.Received(1).BasicPublishAsync(string.Empty, "orders", false,
			Arg.Is<BasicProperties>(p => p.DeliveryMode == DeliveryModes.Transient),
			Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Publisher_confirms_are_on_by_default()
	{
		var (publisher, _, connection, _) = BuildPublisher();

		await publisher.PublishAsync("orders", new TestMessage(7));

		await connection.Received(1).CreateChannelAsync(
			Arg.Is<CreateChannelOptions?>(options => options!.PublisherConfirmationsEnabled
				&& options.PublisherConfirmationTrackingEnabled),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Publisher_confirms_can_be_disabled()
	{
		var (publisher, _, connection, _) = BuildPublisher(options => options.PublisherConfirms = false);

		await publisher.PublishAsync("orders", new TestMessage(7));

		await connection.Received(1).CreateChannelAsync(
			Arg.Is<CreateChannelOptions?>(options => !options!.PublisherConfirmationsEnabled
				&& !options.PublisherConfirmationTrackingEnabled),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Open_channel_is_reused_between_publishes()
	{
		var (publisher, _, connection, _) = BuildPublisher();

		await publisher.PublishAsync("orders", new TestMessage(1));
		await publisher.PublishAsync("orders", new TestMessage(2));

		await connection.Received(1).CreateChannelAsync(
			Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Closed_channel_is_recreated_on_open_connection()
	{
		var (publisher, factory, connection, channel) = BuildPublisher();

		// канал всегда «закрыт»: первая публикация создаёт его, вторая — пересоздаёт
		channel.IsOpen.Returns(false);

		await publisher.PublishAsync("orders", new TestMessage(1));
		await publisher.PublishAsync("orders", new TestMessage(2));

		// соединение при этом живо и пересоздаётся только канал
		await factory.Received(1).CreateAsync(Arg.Any<RabbitMqOption>(), Arg.Any<CancellationToken>());
		await connection.Received(2).CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
		await channel.Received(2).BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Publish_logs_channel_lifecycle()
	{
		var logger = new TestLogger<RabbitMqPublisher>();
		var (publisher, _, _, _) = BuildPublisher(logger: logger);

		await publisher.PublishAsync("orders", new TestMessage(7));

		Assert.Contains(logger.Entries, entry => entry.Message.Contains("Opening publisher connection"));
		Assert.Contains(logger.Entries, entry => entry.Message.Contains("Opening publisher channel"));
	}

	private static (IRabbitMqPublisher Publisher, IRabbitMqConnectionFactory Factory, IConnection Connection, IChannel Channel) BuildPublisher(
		Action<RabbitMqOption>? configure = null, ILogger<RabbitMqPublisher>? logger = null)
	{
		var option = new RabbitMqOption { ConnectionString = "amqp://guest:guest@localhost:5672" };

		configure?.Invoke(option);

		var factory = Substitute.For<IRabbitMqConnectionFactory>();
		var connection = Substitute.For<IConnection>();
		var channel = Substitute.For<IChannel>();

		connection.IsOpen.Returns(true);
		channel.IsOpen.Returns(true);
		connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
			.Returns(channel);
		factory.CreateAsync(Arg.Any<RabbitMqOption>(), Arg.Any<CancellationToken>())
			.Returns(connection);

		return (new RabbitMqPublisher(option, factory, logger), factory, connection, channel);
	}
}
