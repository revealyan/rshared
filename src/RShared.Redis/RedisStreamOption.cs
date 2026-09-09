namespace RShared.Redis;

/// <summary>
/// Consumer group settings for one stream subscription.
/// </summary>
public sealed class RedisStreamOption
{
	/// <summary>
	/// Consumer group name: instances with the same group load balance,
	/// different groups each get every message.
	/// </summary>
	public string Group { get; set; } = "main";

	/// <summary>
	/// In-process attempts of a failed message before dead-lettering.
	/// A process restart starts the count over.
	/// </summary>
	public int MaxRetryCount { get; set; } = 3;

	/// <summary>
	/// Dead letter stream for poison entries: "{stream}.dlq" by default, empty string disables
	/// (a failed entry is then only logged — durable events must not vanish silently, think twice).
	/// </summary>
	public string DeadLetterStream { get; set; } = ".dlq";

	/// <summary>
	/// Idle time after which a pending entry is reclaimed from a lost consumer (XAUTOCLAIM).
	/// </summary>
	public TimeSpan MinIdleBeforeReclaim { get; set; } = TimeSpan.FromMinutes(1);

	/// <summary>
	/// XREADGROUP COUNT per read.
	/// </summary>
	public int BatchSize { get; set; } = 10;

	/// <summary>
	/// Approximate trim on publish (XADD MAXLEN ~). 0 — never trim; an untrimmed stream grows forever.
	/// </summary>
	public long MaxStreamLength { get; set; } = 100_000;
}
