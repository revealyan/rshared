namespace RShared.IdentityKit;

/// <summary>
/// Registration outcome.
/// </summary>
public enum RegisterStatus
{
	/// <summary>
	/// The user has been created and an email confirmation code has been sent.
	/// </summary>
	Success,

	/// <summary>
	/// The email is already taken.
	/// </summary>
	DuplicateEmail,
}

/// <summary>
/// Registration result.
/// </summary>
public sealed record RegisterResult(RegisterStatus Status, Guid UserId);

/// <summary>
/// Users, one time email flows and session invalidation over AuthKit + Orm.
/// Password sign in, external challenges and raw sign in stay in <c>IAuthKit</c>:
/// one feature — one entry point, no duplicated sign in paths.
/// </summary>
public interface IIdentityKit
{
	/// <summary>
	/// Creates a user and sends an email confirmation code. One unit of work:
	/// a failed send rolls the user back.
	/// </summary>
	Task<RegisterResult> RegisterAsync(string email, string password, IEnumerable<string>? roles = null);

	/// <summary>
	/// Issues a passwordless sign in code and sends it. Silent for unknown, disabled
	/// or unconfirmed emails — the flow must not reveal account existence.
	/// </summary>
	Task SendEmailCodeAsync(string email);

	/// <summary>
	/// Signs in with a one time code from the email. Returns false when the code is
	/// unknown, expired, already used or the user cannot sign in.
	/// </summary>
	Task<bool> EmailCodeSignInAsync(string email, string code);

	/// <summary>
	/// Issues a password reset code and sends it. Returns no outcome on purpose —
	/// the flow must not reveal account existence.
	/// </summary>
	Task RequestPasswordResetAsync(string email);

	/// <summary>
	/// Sets a new password by a reset code. Regenerates the security stamp:
	/// older sessions get rejected by the validator. Marks the email confirmed —
	/// receiving the code proves ownership.
	/// </summary>
	Task<bool> ResetPasswordAsync(string email, string code, string newPassword);

	/// <summary>
	/// Re-sends the email confirmation code. Silent when the email is unknown,
	/// disabled or already confirmed.
	/// </summary>
	Task ResendEmailConfirmationAsync(string email);

	/// <summary>
	/// Confirms the email by a one time code.
	/// </summary>
	Task<bool> ConfirmEmailAsync(string email, string code);

	/// <summary>
	/// Sets a new password for the signed in user after checking the current one.
	/// Regenerates the security stamp: older sessions get rejected by the validator.
	/// </summary>
	Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);

	/// <summary>
	/// Enables or disables the user. A disabled user cannot sign in;
	/// a live session gets rejected within the validation interval (and comes back
	/// alive if the user is re-enabled while the cookie still exists).
	/// </summary>
	Task SetEnabledAsync(Guid userId, bool enabled);
}
