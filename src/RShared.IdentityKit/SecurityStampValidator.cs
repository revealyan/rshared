using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using RShared.Orm;

namespace RShared.IdentityKit;

/// <summary>
/// Rejects sessions whose security stamp no longer matches the database or whose user
/// is disabled. Stamp lookups are cached per user for the validation interval,
/// so a password change invalidates sessions within that window, not instantly.
/// Sessions without the stamp claim (plain AuthKit) are left alone.
/// </summary>
internal sealed class SecurityStampValidator<TUser>(
	IEntityRepositoryFactory factory,
	IMemoryCache cache,
	IdentityKitOption option) where TUser : IdentityKitUser
{
	/// <summary>
	/// Cookie validation entry point, wired by AddIdentityKit via PostConfigure.
	/// </summary>
	public async Task ValidateAsync(CookieValidatePrincipalContext context)
	{
		var idText = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		var stampClaim = context.Principal?.FindFirst(IdentityKitClaims.SecurityStamp)?.Value;
		if (idText is null || stampClaim is null || !Guid.TryParse(idText, out var userId))
		{
			return;
		}

		// свежий кэш с тем же штампом — БД не трогаем (окно инвалидации = интервалу)
		if (cache.TryGetValue(userId, out string? cached) && cached == stampClaim)
		{
			return;
		}

		var user = await factory.Create<TUser>().GetAsync(userId);
		if (user is not { Enabled: true } || user.SecurityStamp != stampClaim)
		{
			context.RejectPrincipal();
			return;
		}

		cache.Set(userId, user.SecurityStamp, option.SecurityStampValidationInterval);
	}
}
