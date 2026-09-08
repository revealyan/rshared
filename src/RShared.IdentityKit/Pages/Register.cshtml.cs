using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RShared.IdentityKit.Pages;

/// <summary>
/// Built-in registration page: creates the user, sends a confirmation code
/// and signs the fresh session in. Use your own page and the IIdentityKit
/// service when the built-in markup does not fit.
/// </summary>
public sealed class RegisterModel(IIdentityKit identityKit, IAuthKit authKit) : PageModel
{
	/// <summary>
	/// Where to go after a successful registration
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? ReturnUrl { get; set; }

	/// <summary>
	/// Last registration failure reason shown on the page
	/// </summary>
	public string? Error { get; private set; }

	public IActionResult OnGet()
	{
		return Page();
	}

	public async Task<IActionResult> OnPostAsync(string email, string password)
	{
		var result = await identityKit.RegisterAsync(email, password);
		if (result.Status == RegisterStatus.DuplicateEmail)
		{
			Error = "This email is already registered.";
			return Page();
		}

		// свежая сессия сразу; невозможный отказ входа разрулит редирект на страницу логина
		await authKit.PasswordSignInAsync(email, password);
		return LocalRedirect(SafeReturnUrl);
	}

	private string SafeReturnUrl =>
		string.IsNullOrEmpty(ReturnUrl) || !Url.IsLocalUrl(ReturnUrl) ? "/" : ReturnUrl!;
}
