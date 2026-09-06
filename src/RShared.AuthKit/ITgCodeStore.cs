namespace RShared.AuthKit;

/// <summary>
/// Single use Telegram code storage.
/// The default implementation keeps codes in process memory; register your own
/// implementation (Redis, a database table) before AddAuthKit to share codes across nodes.
/// </summary>
public interface ITgCodeStore
{
	/// <summary>
	/// Issues a code for a Telegram user id
	/// </summary>
	Task<string> IssueAsync(string telegramUserId, TimeSpan lifetime);

	/// <summary>
	/// Takes the code out (single use): returns the Telegram user id, or null when unknown, expired or already used.
	/// </summary>
	Task<string?> TakeAsync(string code);
}
