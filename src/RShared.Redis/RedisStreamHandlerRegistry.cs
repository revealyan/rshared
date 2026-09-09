namespace RShared.Redis;

/// <summary>
/// Stream handlers collected in composition root: message type, handler and its group settings.
/// The publisher consults the handler option of the type for stream trimming.
/// </summary>
internal sealed class RedisStreamHandlerRegistry
{
	private readonly object _gate = new();
	private readonly List<Entry> _entries = [];

	public void Add<TMessage>(Func<TMessage, Task> handler, RedisStreamOption option)
	{
		lock (_gate)
		{
			_entries.Add(new Entry(typeof(TMessage), message => handler((TMessage)message!), option));
		}
	}

	public IReadOnlyCollection<Entry> Snapshot()
	{
		lock (_gate)
		{
			return [.. _entries];
		}
	}

	/// <summary>
	/// The option of the first handler of the type, null when nobody subscribes to it.
	/// </summary>
	public RedisStreamOption? OptionFor(Type messageType)
	{
		lock (_gate)
		{
			return _entries.FirstOrDefault(e => e.MessageType == messageType)?.Option;
		}
	}

	internal sealed record Entry(Type MessageType, Func<object?, Task> Handler, RedisStreamOption Option);
}
