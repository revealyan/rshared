using RShared.IdentityKit;

namespace RShared.IdentityKit;

/// <summary>
/// Issued one time code. Only the HMAC hash of the code is stored, never the code itself.
/// </summary>
public sealed class OneTimeCode
{
	/// <summary>
	/// Surrogate key (version 7 Guid).
	/// </summary>
	public Guid Id { get; set; }

	/// <summary>
	/// What the code proves.
	/// </summary>
	public OneTimeCodePurpose Purpose { get; set; }

	/// <summary>
	/// Delivery channel the code was issued for.
	/// </summary>
	public OneTimeCodeChannel Channel { get; set; }

	/// <summary>
	/// Canonical email or a Telegram user id.
	/// </summary>
	// Stryker disable once String : NRT-заглушка, значение задаёт сервис
	public string Destination { get; set; } = string.Empty;

	/// <summary>
	/// HMAC-SHA256(pepper, code) in hex.
	/// </summary>
	// Stryker disable once String : NRT-заглушка, значение задаёт сервис
	public string CodeHash { get; set; } = string.Empty;

	/// <summary>
	/// UTC expiry.
	/// </summary>
	public DateTime ExpiresAt { get; set; }

	/// <summary>
	/// UTC consumption time; null while the code is active.
	/// </summary>
	public DateTime? ConsumedAt { get; set; }
}
