namespace RShared.IdentityKit;

/// <summary>
/// Consumer seam: maps a proven external identity to an application user.
/// Implementations find or create the user in the consumer storage
/// (ASP.NET Core Identity, a database, an identity file — anything).
/// Returns null when the identity is not allowed to sign in.
/// </summary>
public interface IAuthKitUserResolver
{
	Task<AuthKitUser?> ResolveAsync(ExternalIdentity identity);
}
