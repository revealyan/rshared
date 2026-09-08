using RShared.AuthKit;

namespace RShared.IdentityKit;

/// <summary>
/// Consumer seam: delivers one time codes by email. Message texts are fully consumer-side.
/// Register an implementation in DI; email flows throw when it is missing.
/// </summary>
public interface IEmailSender
{
	/// <summary>
	/// Sends the code; the purpose shapes the message wording, not the transport.
	/// </summary>
	Task SendCodeAsync(string to, OneTimeCodePurpose purpose, string code);
}
