using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RShared.AuthKit.Pages;

/// <summary>
/// Built-in login page: password form, Telegram code form, Google challenge.
/// Use your own page and the IAuthKit service when the built-in markup does not fit.
/// </summary>
public sealed class LoginModel(IAuthKit authKit) : PageModel
{
	/// <summary>
	/// Where to go after a successful sign in
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? ReturnUrl { get; set; }

	/// <summary>
	/// Last sign in failure reason shown on the page
	/// </summary>
	public string? Error { get; private set; }

	public IActionResult OnGet()
	{
		return Page();
	}

	public async Task<IActionResult> OnGetGoogleAsync()
	{
		await authKit.ChallengeGoogleAsync(SafeReturnUrl);
		return new EmptyResult();
	}

	public async Task<IActionResult> OnPostPasswordAsync(string login, string password)
	{
		if (!await authKit.PasswordSignInAsync(login, password))
		{
			Error = "Invalid login or password.";
			return Page();
		}

		return LocalRedirect(SafeReturnUrl);
	}

	public async Task<IActionResult> OnPostTelegramAsync(string code)
	{
		if (!await authKit.TelegramSignInAsync(code))
		{
			Error = "The code is unknown, expired or already used.";
			return Page();
		}

		return LocalRedirect(SafeReturnUrl);
	}

	private string SafeReturnUrl =>
		string.IsNullOrEmpty(ReturnUrl) || !Url.IsLocalUrl(ReturnUrl) ? "/" : ReturnUrl!;
}
