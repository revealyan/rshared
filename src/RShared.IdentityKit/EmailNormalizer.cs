namespace RShared.IdentityKit;

/// <summary>
/// Email canonical form shared by every IdentityKit lookup.
/// </summary>
internal static class EmailNormalizer
{
	/// <summary>
	/// Trims and lowercases invariantly: the canonical form stored in the users table.
	/// </summary>
	public static string Normalize(string email)
	{
		return email.Trim().ToLowerInvariant();
	}
}
