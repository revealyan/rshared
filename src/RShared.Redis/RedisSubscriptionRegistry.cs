namespace RShared.Redis;

/// <summary>
/// Subscriptions collected in composition root: message type + compiled handler.
/// The subscriber service groups entries by type at start.
/// </summary>
internal sealed class RedisSubscriptionRegistry
{
	private readonly object _gate = new();
	private readonly List<Entry> _entries = [];

	public void Add<TMessage>(Func<TMessage, Task> handler)
	{
		lock (_gate)
		{
			_entries.Add(new Entry(typeof(TMessage), message => handler((TMessage)message!)));
		}
	}

	public IReadOnlyCollection<Entry> Snapshot()
	{
		lock (_gate)
		{
			return [.. _entries];
		}
	}

	internal sealed record Entry(Type MessageType, Func<object?, Task> Handler);
}
