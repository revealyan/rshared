using Microsoft.Extensions.Logging;

namespace RShared.RabbitMq;

public abstract class RabbitMqEventWrapperMessageProcessor
	: RabbitMqEventWrapperMessageProcessor<RabbitMqEventWrapper>
{
	protected RabbitMqEventWrapperMessageProcessor(ILogger<RabbitMqEventWrapperMessageProcessor<RabbitMqEventWrapper>> logger,
			IDefaultRabbitMqMessageSerializer messageSerializer)
		: base(logger, messageSerializer)
	{
		
	}
}

public abstract class RabbitMqEventWrapperMessageProcessor<TEventWrapper>
	: IRabbitMqMessageProcessor
	where TEventWrapper : RabbitMqEventWrapper, new()
{
	protected readonly ILogger<RabbitMqEventWrapperMessageProcessor<TEventWrapper>> Logger;
	protected readonly IDefaultRabbitMqMessageSerializer MessageSerializer;

	protected RabbitMqEventWrapperMessageProcessor(ILogger<RabbitMqEventWrapperMessageProcessor<TEventWrapper>> logger,
			IDefaultRabbitMqMessageSerializer messageSerializer)
	{
		Logger = logger;
		MessageSerializer = messageSerializer;
	}

	public abstract string QueueId { get; }
	protected abstract Task ProcessEventAsync(TEventWrapper wrapper, CancellationToken cancellationToken = default);

	public virtual async Task<bool> ProcessAsync(RabbitMqMessage message, CancellationToken cancellationToken = default)
	{
		try
		{
			var wrapper = await MessageSerializer.DeserializeAsync<TEventWrapper>(message, cancellationToken)
				?? throw new InvalidCastException($"Can't deserialize to {typeof(TEventWrapper)}");

			await ProcessEventAsync(wrapper, cancellationToken);

			return true;
		}
		catch (Exception exc)
		{
			Logger.LogError(exc, "Error at processing event");
		}

		return false;
	}
}
