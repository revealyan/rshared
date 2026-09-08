using Xunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Options;
using NSubstitute;
using RShared.IdentityKit;
using RShared.IdentityKit.Pages;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Встроенные страницы аккаунта: регистрация с автологином, подтверждение,
/// сброс пароля; нейтральность forgot-флоу, редиректы только в локальные пути
/// </summary>
public sealed class AccountPagesTests
{
	private static IUrlHelper Url(params (string? Url, bool Local)[] knownUrls)
	{
		var url = Substitute.For<IUrlHelper>();
		foreach (var (urlValue, local) in knownUrls)
		{
			url.IsLocalUrl(urlValue).Returns(local);
		}

		return url;
	}

	[Fact]
	public void Register_OnGet_renders_the_page()
	{
		Assert.IsType<PageResult>(new RegisterModel(Substitute.For<IIdentityKit>(), Substitute.For<IAuthKit>()).OnGet());
	}

	[Fact]
	public async Task Register_OnPost_signs_in_and_redirects_locally()
	{
		var identity = Substitute.For<IIdentityKit>();
		identity.RegisterAsync("a@b.c", "secret").Returns(new RegisterResult(RegisterStatus.Success, Guid.NewGuid()));
		var authKit = Substitute.For<IAuthKit>();
		authKit.PasswordSignInAsync("a@b.c", "secret").Returns(true);
		var model = new RegisterModel(identity, authKit) { Url = Url(("/back", true)), ReturnUrl = "/back" };

		var result = await model.OnPostAsync("a@b.c", "secret");

		var redirect = Assert.IsType<LocalRedirectResult>(result);
		Assert.Equal("/back", redirect.Url);
		await authKit.Received(1).PasswordSignInAsync("a@b.c", "secret");
	}

	[Fact]
	public async Task Register_OnPost_duplicate_email_shows_error()
	{
		var identity = Substitute.For<IIdentityKit>();
		identity.RegisterAsync("a@b.c", Arg.Any<string>()).Returns(new RegisterResult(RegisterStatus.DuplicateEmail, Guid.Empty));
		var model = new RegisterModel(identity, Substitute.For<IAuthKit>());

		var result = await model.OnPostAsync("a@b.c", "secret");

		Assert.IsType<PageResult>(result);
		Assert.Equal("This email is already registered.", model.Error);
	}

	[Fact]
	public async Task Register_OnPost_falls_back_to_root_for_non_local_url()
	{
		var identity = Substitute.For<IIdentityKit>();
		identity.RegisterAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new RegisterResult(RegisterStatus.Success, Guid.NewGuid()));
		var model = new RegisterModel(identity, Substitute.For<IAuthKit>())
		{
			Url = Url(("https://evil.example", false)),
			ReturnUrl = "https://evil.example",
		};

		var result = await model.OnPostAsync("a@b.c", "secret");

		var redirect = Assert.IsType<LocalRedirectResult>(result);
		Assert.Equal("/", redirect.Url);
	}

	[Fact]
	public void ConfirmEmail_OnGet_renders_the_page()
	{
		Assert.IsType<PageResult>(new ConfirmEmailModel(Substitute.For<IIdentityKit>(), Options.Create(new AuthKitOption())).OnGet());
	}

	[Fact]
	public async Task ConfirmEmail_OnPost_redirects_to_the_login_path()
	{
		var identity = Substitute.For<IIdentityKit>();
		identity.ConfirmEmailAsync("a@b.c", "ABCD2345").Returns(true);
		var option = Options.Create(new AuthKitOption { LoginPath = "/sign-in" });
		var model = new ConfirmEmailModel(identity, option);

		var result = await model.OnPostAsync("a@b.c", "ABCD2345");

		var redirect = Assert.IsType<LocalRedirectResult>(result);
		Assert.Equal("/sign-in", redirect.Url);
	}

	[Fact]
	public async Task ConfirmEmail_OnPost_wrong_code_shows_error()
	{
		var identity = Substitute.For<IIdentityKit>();
		identity.ConfirmEmailAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
		var model = new ConfirmEmailModel(identity, Options.Create(new AuthKitOption()));

		var result = await model.OnPostAsync("a@b.c", "WRONG");

		Assert.IsType<PageResult>(result);
		Assert.Equal("The code is unknown, expired or already used.", model.Error);
	}

	[Fact]
	public void ForgotPassword_OnGet_renders_the_page()
	{
		var model = new ForgotPasswordModel(Substitute.For<IIdentityKit>());

		Assert.IsType<PageResult>(model.OnGet());
		Assert.False(model.Requested);
	}

	[Fact]
	public async Task ForgotPassword_OnPost_is_neutral_about_the_outcome()
	{
		var identity = Substitute.For<IIdentityKit>();
		var model = new ForgotPasswordModel(identity);

		var result = await model.OnPostAsync("a@b.c");

		Assert.IsType<PageResult>(result);
		Assert.True(model.Requested);
		await identity.Received(1).RequestPasswordResetAsync("a@b.c");
	}

	[Fact]
	public void ResetPassword_OnGet_renders_the_page()
	{
		Assert.IsType<PageResult>(new ResetPasswordModel(Substitute.For<IIdentityKit>(), Options.Create(new AuthKitOption())).OnGet());
	}

	[Fact]
	public async Task ResetPassword_OnPost_redirects_to_the_login_path()
	{
		var identity = Substitute.For<IIdentityKit>();
		identity.ResetPasswordAsync("a@b.c", "ABCD2345", "newpass").Returns(true);
		var option = Options.Create(new AuthKitOption { LoginPath = "/sign-in" });
		var model = new ResetPasswordModel(identity, option);

		var result = await model.OnPostAsync("a@b.c", "ABCD2345", "newpass");

		var redirect = Assert.IsType<LocalRedirectResult>(result);
		Assert.Equal("/sign-in", redirect.Url);
	}

	[Fact]
	public async Task ResetPassword_OnPost_wrong_code_shows_error()
	{
		var identity = Substitute.For<IIdentityKit>();
		identity.ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
		var model = new ResetPasswordModel(identity, Options.Create(new AuthKitOption()));

		var result = await model.OnPostAsync("a@b.c", "WRONG", "newpass");

		Assert.IsType<PageResult>(result);
		Assert.Equal("The code is unknown, expired or already used.", model.Error);
	}
}
