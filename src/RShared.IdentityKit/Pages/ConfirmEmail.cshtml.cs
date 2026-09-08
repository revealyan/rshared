using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace RShared.IdentityKit.Pages;

/// <summary>
/// Built-in email confirmation page: the code arrives by IEmailSender.
/// </summary>
public sealed class ConfirmEmailModel(
	IIdentityKit identityKit,
	IOptions<AuthKitOption> option) : PageModel
{
	/// <summary>
	/// Email being confirmed, pre-filled from the query
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? Email { get; set; }

	/// <summary>
	/// Last confirmation failure reason shown on the page
	/// </summary>
	public string? Error { get; private set; }

	public IActionResult OnGet()
	{
		return Page();
	}

	public async Task<IActionResult> OnPostAsync(string email, string code)
	{
		if (!await identityKit.ConfirmEmailAsync(email, code))
		{
			Error = "The code is unknown, expired or already used.";
			return Page();
		}

		return LocalRedirect(option.Value.LoginPath);
	}
}
