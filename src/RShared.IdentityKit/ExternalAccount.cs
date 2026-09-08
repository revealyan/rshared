namespace RShared.IdentityKit;

/// <summary>
/// Link between an application user and a proven external identity (a provider account).
/// </summary>
public sealed class ExternalAccount
{
	/// <summary>
	/// Surrogate key (version 7 Guid).
	/// </summary>
	public Guid Id { get; set; }

	/// <summary>
	/// Provider name, e.g. "google". Unique per (Provider, ExternalId).
	/// </summary>
	// Stryker disable once String : NRT-заглушка, значение задаёт сервис
	public string Provider { get; set; } = string.Empty;

	/// <summary>
	/// Account id inside the provider.
	/// </summary>
	// Stryker disable once String : NRT-заглушка, значение задаёт сервис
	public string ExternalId { get; set; } = string.Empty;

	/// <summary>
	/// Owning user.
	/// </summary>
	public Guid UserId { get; set; }

	/// <summary>
	/// UTC creation time.
	/// </summary>
	public DateTime CreatedAt { get; set; }
}
