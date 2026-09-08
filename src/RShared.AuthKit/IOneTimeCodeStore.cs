namespace RShared.AuthKit;

/// <summary>
/// What the code proves; a store never mixes codes of different purposes.
/// </summary>
public enum OneTimeCodePurpose
{
	/// <summary>
	/// Sign in with a code delivered to the destination.
	/// </summary>
	Login,

	/// <summary>
	/// Confirm ownership of an email address.
	/// </summary>
	EmailConfirm,

	/// <summary>
	/// Prove ownership of an email address to set a new password.
	/// </summary>
	PasswordReset,
}

/// <summary>
/// Delivery channel the code was issued for; the destination is an email address or a Telegram user id.
/// </summary>
public enum OneTimeCodeChannel
{
	/// <summary>
	/// The destination is an email address.
	/// </summary>
	Email,

	/// <summary>
	/// The destination is a Telegram user id.
	/// </summary>
	Telegram,
}

/// <summary>
/// Single use code storage. A code belongs to a triple (channel, purpose, destination):
/// issuing a new code for the same triple annuls the previous one.
/// The default implementation keeps codes in process memory; register your own implementation
/// (Redis, a database table) before AddAuthKit to share codes across nodes.
/// </summary>
public interface IOneTimeCodeStore
{
	/// <summary>
	/// Issues a code; returns the code, delivery stays on the consumer.
	/// </summary>
	Task<string> IssueAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string destination, TimeSpan lifetime);

	/// <summary>
	/// Takes the code out (single use) when the destination is not known upfront
	/// (the login page knows only the code): returns the destination,
	/// or null when the code is unknown, expired or already used.
	/// </summary>
	Task<string?> TakeAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string code);

	/// <summary>
	/// Takes the code out (single use) for a known destination: true only when the code matches
	/// the whole triple. A code issued for another purpose never satisfies this.
	/// </summary>
	Task<bool> TakeAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string destination, string code);
}
