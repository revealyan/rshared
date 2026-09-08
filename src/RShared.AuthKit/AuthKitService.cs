using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace RShared.AuthKit;

/// <summary>
/// Sign in implementation: cookie sessions, one-time Telegram codes, provider challenges.
/// Password validation and user resolution are delegated to consumer seams.
/// </summary>
internal sealed class AuthKitService(
	IHttpContextAccessor httpContextAccessor,
	IAuthKitUserResolver userResolver,
	IAuthKitPasswordStore? passwordStore,
	AuthKitOption option,
	IOneTimeCodeStore codes) : IAuthKit
{
	internal const string GoogleScheme = "RShared.AuthKit.Google";

	private HttpContext? Http => httpContextAccessor.HttpContext;

	public async Task<bool> PasswordSignInAsync(string login, string password)
	{
		if (option.Password is null || passwordStore is null || string.IsNullOrWhiteSpace(login))
		{
			return false;
		}

		if (!await passwordStore.ValidateAsync(login, password))
		{
			return false;
		}

		// the password proves ownership of the login, so the login acts as the external id
		var user = await userResolver.ResolveAsync(new ExternalIdentity("password", login));
		if (user is null)
		{
			return false;
		}

		await SignInAsync(user, option.Password.SessionLifetime);
		return true;
	}

	public Task<string> IssueTelegramCodeAsync(long telegramUserId)
	{
		if (option.Telegram is null)
		{
			throw new InvalidOperationException("AuthKit: the telegram provider is disabled");
		}

		return codes.IssueAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login,
			telegramUserId.ToString(CultureInfo.InvariantCulture), option.Telegram.CodeLifetime);
	}

	public async Task<bool> TelegramSignInAsync(string code)
	{
		var telegramUserId = await codes.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, code.Trim());
		if (telegramUserId is null)
		{
			return false;
		}

		var user = await userResolver.ResolveAsync(new ExternalIdentity("telegram", telegramUserId));
		if (user is null)
		{
			return false;
		}

		await SignInAsync(user, option.Telegram?.SessionLifetime ?? TimeSpan.FromDays(14));
		return true;
	}

	public Task ChallengeGoogleAsync(string returnPath)
	{
		var http = Http ?? throw new InvalidOperationException("AuthKit: no http context for a Google challenge");

		// guard от open redirect: после колбэка Google уходим только в локальный путь
		return http.ChallengeAsync(GoogleScheme, new AuthenticationProperties
		{
			RedirectUri = IsLocalUrl(returnPath) ? returnPath : option.DefaultReturnPath,
		});
	}

	public async Task SignInAsync(AuthKitUser user, TimeSpan? sessionLifetime = null)
	{
		var http = Http ?? throw new InvalidOperationException("AuthKit: no http context for a sign in");

		var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id) };
		if (user.DisplayName is not null)
		{
			claims.Add(new(ClaimTypes.Name, user.DisplayName));
		}

		// клеймы резолвера (роли, security stamp) идут в сессию как есть
		if (user.Claims is { } extra)
		{
			claims.AddRange(extra);
		}

		var props = new AuthenticationProperties { IsPersistent = true };
		if (sessionLifetime is { } lifetime)
		{
			props.ExpiresUtc = DateTimeOffset.UtcNow.Add(lifetime);
		}

		await http.SignInAsync(option.Scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, option.Scheme)), props);
	}

	public Task SignOutAsync()
	{
		var http = Http ?? throw new InvalidOperationException("AuthKit: no http context for a sign out");
		return http.SignOutAsync(option.Scheme);
	}

	/// <summary>
	/// Bridge for the Google handler: turns a proven external id into a session.
	/// Called from AuthKitExtensions (OnTicketReceived), not from pages.
	/// </summary>
	internal async Task<bool> ResolveExternalSignInAsync(ExternalIdentity identity, TimeSpan sessionLifetime)
	{
		var user = await userResolver.ResolveAsync(identity);
		if (user is null)
		{
			return false;
		}

		await SignInAsync(user, sessionLifetime);
		return true;
	}

	/// <summary>
	/// Google ticket → external identity, null when the ticket has no name identifier claim.
	/// </summary>
	internal static ExternalIdentity? MapGoogleTicket(ClaimsPrincipal? principal)
	{
		var externalId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (externalId is null)
		{
			return null;
		}

		return new ExternalIdentity("google", externalId,
			principal?.FindFirst(ClaimTypes.Email)?.Value,
			principal?.FindFirst(ClaimTypes.Name)?.Value);
	}

	/// <summary>
	/// Local url check (the same shape as Url.IsLocalUrl): only app relative paths pass
	/// </summary>
	internal static bool IsLocalUrl(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return false;
		}

		if (url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')))
		{
			return true;
		}

		return url.Length > 1 && url[0] == '~' && url[1] == '/';
	}
}
