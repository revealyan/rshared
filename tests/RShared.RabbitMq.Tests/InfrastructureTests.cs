using System.Text.Json;
using Xunit;

namespace RShared.RabbitMq.Tests;

public class InfrastructureTests
{
	[Fact]
	public void BuildFactory_maps_option_to_client_factory()
	{
		var option = new RabbitMqOption
		{
			ConnectionString = "amqp://guest:guest@localhost:5672/rshared",
			ClientName = "orders-api",
		};

		var factory = RabbitMqConnectionFactory.BuildFactory(option);

		Assert.Equal("orders-api", factory.ClientProvidedName);
		Assert.Equal(new Uri("amqp://guest:guest@localhost:5672/rshared"), factory.Uri);
	}

	[Fact]
	public void Deserialize_treats_null_payload_as_poison()
	{
		var exception = Assert.Throws<JsonException>(
			() => RabbitMqJson.Deserialize(typeof(TestMessage), "null"u8.ToArray(), null));

		Assert.Contains(nameof(TestMessage), exception.Message);
	}

	[Fact]
	public void Serialize_round_trips_message_body()
	{
		var body = RabbitMqJson.Serialize(new TestMessage(7), null);

		var message = Assert.IsType<TestMessage>(RabbitMqJson.Deserialize(typeof(TestMessage), body, null));

		Assert.Equal(7, message.OrderId);
	}
}
