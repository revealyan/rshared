using RShared.IdentityKit;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Захватывает отправленные коды
/// </summary>
internal sealed class FakeEmailSender : IEmailSender
{
	public List<(string To, OneTimeCodePurpose Purpose, string Code)> Sent { get; } = [];

	public Task SendCodeAsync(string to, OneTimeCodePurpose purpose, string code)
	{
		Sent.Add((to, purpose, code));
		return Task.CompletedTask;
	}
}
