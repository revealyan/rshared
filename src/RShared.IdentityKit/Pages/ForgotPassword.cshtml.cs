using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RShared.IdentityKit.Pages;

/// <summary>
/// Built-in "forgot password" page: requests a reset code.
/// The outcome message is neutral on purpose — the flow must not reveal account existence.
/// </summary>
public sealed class ForgotPasswordModel(IIdentityKit identityKit) : PageModel
{
	/// <summary>
	/// Neutral confirmation shown after the request
	/// </summary>
	public bool Requested { get; private set; }

	public IActionResult OnGet()
	{
		return Page();
	}

	public async Task<IActionResult> OnPostAsync(string email)
	{
		await identityKit.RequestPasswordResetAsync(email);
		Requested = true;
		return Page();
	}
}
