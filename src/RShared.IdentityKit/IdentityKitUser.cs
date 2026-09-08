namespace RShared.IdentityKit;

/// <summary>
/// Application user entity. Use as is or subclass to add profile fields
/// (map the subclass with <see cref="ModelBuilderExtensions.ApplyIdentityKit{TUser}"/>).
/// Email is stored in the canonical form (trimmed, lower invariant); timestamps are UTC.
/// </summary>
public class IdentityKitUser
{
	/// <summary>
	/// Surrogate key. Version 7 Guids are assigned by the service, not by the database.
	/// </summary>
	public Guid Id { get; set; }

	/// <summary>
	/// Canonical email (trimmed, lower invariant); unique across users.
	/// </summary>
	// Stryker disable once String : NRT-заглушка, значение задаёт сервис
	public string Email { get; set; } = string.Empty;

	/// <summary>
	/// PasswordHasher output; null for external-only accounts.
	/// </summary>
	public string? PasswordHash { get; set; }

	/// <summary>
	/// Whether ownership of the email has been proven.
	/// </summary>
	public bool EmailConfirmed { get; set; }

	/// <summary>
	/// Role names that land in the session as role claims. No role management is included.
	/// </summary>
	public List<string> Roles { get; set; } = [];

	/// <summary>
	/// Regenerated on password changes: sessions issued before the change
	/// are rejected by the security stamp validator.
	/// </summary>
	// Stryker disable once String : NRT-заглушка, значение задаёт сервис
	public string SecurityStamp { get; set; } = string.Empty;

	/// <summary>
	/// Disabled users cannot sign in and their live sessions get rejected.
	/// </summary>
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// UTC creation time.
	/// </summary>
	public DateTime CreatedAt { get; set; }
}
