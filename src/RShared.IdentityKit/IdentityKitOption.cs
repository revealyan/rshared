using RShared.IdentityKit;

namespace RShared.IdentityKit;

/// <summary>
/// IdentityKit dependency injection configuration options.
/// </summary>
public sealed class IdentityKitOption
{
	/// <summary>
	/// Cookie scheme of the AuthKit session to validate. Must match <c>AuthKitOption.Scheme</c>.
	/// </summary>
	public string SessionScheme { get; set; } = AuthKitOption.DefaultScheme;

	/// <summary>
	/// Secret mixed into one time code hashes. The hash lookup works only while the pepper is
	/// stable; rotation burns active codes (not passwords). Required, must not be empty.
	/// </summary>
	public string CodeHashPepper { get; set; } = string.Empty;

	/// <summary>
	/// Lifetime of issued one time codes, shared by all purposes.
	/// </summary>
	public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

	/// <summary>
	/// Lifetime of sessions issued for email code sign ins.
	/// </summary>
	public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);

	/// <summary>
	/// How often a live session revalidates its security stamp against the database.
	/// </summary>
	public TimeSpan SecurityStampValidationInterval { get; set; } = TimeSpan.FromMinutes(30);

	/// <summary>
	/// Whether the first external sign in creates the user.
	/// When off, unknown external identities are rejected until somebody links them.
	/// </summary>
	public bool CreateUsersOnFirstExternalSignIn { get; set; } = true;

	/// <summary>
	/// Link an external identity from a verified-email provider (Google) to an existing user
	/// by email. Off by default: a provider mistake would let a wrong person in.
	/// </summary>
	public bool LinkByEmail { get; set; }
}
