using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace RShared.IdentityKit.Pages;

/// <summary>
/// Built-in password reset page: the code arrives by IEmailSender,
/// the new password invalidates older sessions (security stamp rotation).
/// </summary>
public sealed class ResetPasswordModel(
	IIdentityKit identityKit,
	IOptions<AuthKitOption> option) : PageModel
{
	/// <summary>
	/// Email being reset, pre-filled from the query
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? Email { get; set; }

	/// <summary>
	/// Last reset failure reason shown on the page
	/// </summary>
	public string? Error { get; private set; }

	public IActionResult OnGet()
	{
		return Page();
	}

	public async Task<IActionResult> OnPostAsync(string email, string code, string password)
	{
		if (!await identityKit.ResetPasswordAsync(email, code, password))
		{
			Error = "The code is unknown, expired or already used.";
			return Page();
		}

		return LocalRedirect(option.Value.LoginPath);
	}
}
