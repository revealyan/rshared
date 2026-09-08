using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using RShared.AuthKit;
using NSubstitute;
using RShared.IdentityKit;

using Xunit;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Парольный вход через IAuthKit.PasswordSignInAsync с реальной SQLite:
/// канонизация логина, инвалид, дубль email, внешний юзер без пароля, клеймы сессии
/// </summary>
public sealed class PasswordTests
{
	[Fact]
	public async Task Register_then_password_sign_in()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();

		var result = await kit.RegisterAsync("A@B.C", "secret");

		Assert.Equal(RegisterStatus.Success, result.Status);
		Assert.True(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "secret"));
		await fixture.Auth.Received(1).SignInAsync(Arg.Any<HttpContext>(), "RShared.AuthKit",
			Arg.Is<ClaimsPrincipal>(p => p.FindFirst(ClaimTypes.NameIdentifier)!.Value == result.UserId.ToString()),
			Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task Wrong_password_is_rejected()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await kit.RegisterAsync("a@b.c", "secret");

		Assert.False(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "wrong"));
		await fixture.Auth.DidNotReceiveWithAnyArgs().SignInAsync(default!, default!, default!, default!);
	}

	[Fact]
	public async Task Disabled_user_is_rejected()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");

		await kit.SetEnabledAsync(registered.UserId, false);

		Assert.False(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "secret"));
	}

	[Fact]
	public async Task Duplicate_email_is_reported()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await kit.RegisterAsync("a@b.c", "secret");

		var second = await kit.RegisterAsync("a@b.c", "another");

		Assert.Equal(RegisterStatus.DuplicateEmail, second.Status);
	}

	[Fact]
	public async Task External_only_user_cannot_sign_in_with_password()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1", "guy@x.y"));

		Assert.False(await scope.Get<IAuthKit>().PasswordSignInAsync("guy@x.y", "secret"));
	}

	[Fact]
	public async Task Roles_and_stamp_land_in_session_claims()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret", ["admin"]);

		Assert.True(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "secret"));
		await fixture.Auth.Received(1).SignInAsync(Arg.Any<HttpContext>(), "RShared.AuthKit",
			Arg.Is<ClaimsPrincipal>(p =>
				p.FindFirst(ClaimTypes.Role)!.Value == "admin"
				&& p.FindFirst(IdentityKitClaims.SecurityStamp) != null),
			Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task Password_store_rejects_empty_login_directly()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IAuthKitPasswordStore>();

		// AuthKit отсекает пустой логин раньше, стор проверяет и это (прямой вызов шва)
		Assert.False(await store.ValidateAsync("", "secret"));
		Assert.False(await store.ValidateAsync("  ", "secret"));
	}
}
