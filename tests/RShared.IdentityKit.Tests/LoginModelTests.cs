using Xunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using RShared.IdentityKit;
using RShared.IdentityKit.Pages;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Встроенная страница логина: редиректы только в локальные пути, ошибки провайдеров
/// </summary>
public sealed class LoginModelTests
{
	private static LoginModel Build(IAuthKit kit, params (string? Url, bool Local)[] knownUrls)
	{
		var url = Substitute.For<IUrlHelper>();
		foreach (var (urlValue, local) in knownUrls)
		{
			url.IsLocalUrl(urlValue).Returns(local);
		}

		return new LoginModel(kit) { Url = url };
	}

	[Fact]
	public void OnGet_renders_the_page()
	{
		var model = Build(Substitute.For<IAuthKit>());

		Assert.IsType<PageResult>(model.OnGet());
	}

	[Fact]
	public async Task OnPostPassword_redirects_to_local_return_url()
	{
		var kit = Substitute.For<IAuthKit>();
		kit.PasswordSignInAsync("bob", "secret").Returns(true);
		var model = Build(kit, ("/back", true));
		model.ReturnUrl = "/back";

		var result = await model.OnPostPasswordAsync("bob", "secret");

		var redirect = Assert.IsType<LocalRedirectResult>(result);
		Assert.Equal("/back", redirect.Url);
	}

	[Fact]
	public async Task OnPostPassword_falls_back_to_root_for_non_local_url()
	{
		var kit = Substitute.For<IAuthKit>();
		kit.PasswordSignInAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var model = Build(kit, ("https://evil.example", false));
		model.ReturnUrl = "https://evil.example";

		var result = await model.OnPostPasswordAsync("bob", "secret");

		var redirect = Assert.IsType<LocalRedirectResult>(result);
		Assert.Equal("/", redirect.Url);
	}

	[Fact]
	public async Task OnPostPassword_falls_back_to_root_for_missing_url()
	{
		var kit = Substitute.For<IAuthKit>();
		kit.PasswordSignInAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var model = Build(kit);

		var result = await model.OnPostPasswordAsync("bob", "secret");

		var redirect = Assert.IsType<LocalRedirectResult>(result);
		Assert.Equal("/", redirect.Url);
	}

	[Fact]
	public async Task OnPostPassword_shows_error_on_failure()
	{
		var kit = Substitute.For<IAuthKit>();
		kit.PasswordSignInAsync("bob", "secret").Returns(false);
		var model = Build(kit);

		var result = await model.OnPostPasswordAsync("bob", "secret");

		Assert.IsType<PageResult>(result);
		Assert.Equal("Invalid login or password.", model.Error);
	}

	[Fact]
	public async Task OnPostTelegram_redirects_to_local_return_url()
	{
		var kit = Substitute.For<IAuthKit>();
		kit.TelegramSignInAsync("ABCD2345").Returns(true);
		var model = Build(kit, ("/back", true));
		model.ReturnUrl = "/back";

		var result = await model.OnPostTelegramAsync("ABCD2345");

		var redirect = Assert.IsType<LocalRedirectResult>(result);
		Assert.Equal("/back", redirect.Url);
	}

	[Fact]
	public async Task OnPostTelegram_shows_error_on_failure()
	{
		var kit = Substitute.For<IAuthKit>();
		kit.TelegramSignInAsync("WRONG").Returns(false);
		var model = Build(kit);
		model.ReturnUrl = "/back";

		var result = await model.OnPostTelegramAsync("WRONG");

		Assert.IsType<PageResult>(result);
		Assert.Equal("The code is unknown, expired or already used.", model.Error);
	}

	[Fact]
	public async Task OnGetGoogle_challenges_with_safe_return_url()
	{
		var kit = Substitute.For<IAuthKit>();
		var model = Build(kit, ("/back", true));
		model.ReturnUrl = "/back";

		var result = await model.OnGetGoogleAsync();

		Assert.IsType<EmptyResult>(result);
		await kit.Received(1).ChallengeGoogleAsync("/back");
	}

	[Fact]
	public async Task OnGetGoogle_challenges_to_root_without_return_url()
	{
		var kit = Substitute.For<IAuthKit>();
		var model = Build(kit);

		await model.OnGetGoogleAsync();

		await kit.Received(1).ChallengeGoogleAsync("/");
	}
}
