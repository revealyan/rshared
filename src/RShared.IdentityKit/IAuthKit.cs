using System.Security.Claims;

namespace RShared.IdentityKit;

/// <summary>
/// External identity proven by a provider: a provider name and an account id inside it.
/// AuthKit never decides who the application user is — <see cref="IAuthKitUserResolver"/> does.
/// </summary>
public sealed record ExternalIdentity(string Provider, string Id, string? Email = null, string? Name = null);

/// <summary>
/// Application user resolved from an external identity.
/// Optional <see cref="Claims"/> land in the session as is: the resolver knows the user,
/// AuthKit stays ignorant of roles and stamps.
/// </summary>
public sealed record AuthKitUser(string Id, string? DisplayName = null, IReadOnlyList<Claim>? Claims = null);

/// <summary>
/// Sign in / sign out operations used by both the built-in login page and consumer pages.
/// </summary>
public interface IAuthKit
{
	/// <summary>
	/// Signs in with a login and a password.
	/// The password itself is validated by the consumer <see cref="IAuthKitPasswordStore"/>.
	/// Returns false when the pair is not valid.
	/// </summary>
	Task<bool> PasswordSignInAsync(string login, string password);

	/// <summary>
	/// Issues a one-time Telegram code for a Telegram user id.
	/// The consumer bot delivers the code to the user (AuthKit has no Telegram connection of its own).
	/// </summary>
	Task<string> IssueTelegramCodeAsync(long telegramUserId);

	/// <summary>
	/// Signs in with a one-time Telegram code entered by the user.
	/// A code is single use and expires. Returns false when the code is unknown, expired or already used.
	/// </summary>
	Task<bool> TelegramSignInAsync(string code);

	/// <summary>
	/// Starts the Google OAuth challenge.
	/// </summary>
	Task ChallengeGoogleAsync(string returnPath);

	/// <summary>
	/// Signs the resolved user in (issues the cookie session).
	/// </summary>
	Task SignInAsync(AuthKitUser user, TimeSpan? sessionLifetime = null);

	/// <summary>
	/// Signs out (drops the cookie session).
	/// </summary>
	Task SignOutAsync();
}
