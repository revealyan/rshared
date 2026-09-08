using System.Security.Claims;

namespace RShared.IdentityKit;

/// <summary>
/// Claim names used by IdentityKit.
/// </summary>
public static class IdentityKitClaims
{
	/// <summary>
	/// Session claim carrying the security stamp; the validator compares it with the stored one.
	/// </summary>
	public const string SecurityStamp = "RShared.IdentityKit:securityStamp";
}
