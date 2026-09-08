using Xunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RShared.IdentityKit;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Сервис входа: пароль, коды Telegram, Google-челлендж, клеймы и времена жизни сессий
/// </summary>
public sealed class AuthKitServiceTests
{
	// имя схемы — публичный контракт, захардкожено специально, не выводится из AuthKitOption
	private const string Scheme = "RShared.AuthKit";

	private static (AuthKitService Kit, IAuthenticationService Auth, DefaultHttpContext Http) Build(
		Action<AuthKitOption>? configure = null,
		IAuthKitUserResolver? resolver = null,
		IAuthKitPasswordStore? store = null,
		IOneTimeCodeStore? codes = null)
	{
		var option = new AuthKitOption();
		configure?.Invoke(option);

		var http = new DefaultHttpContext();
		var auth = Substitute.For<IAuthenticationService>();
		http.RequestServices = new ServiceCollection().AddSingleton(auth).BuildServiceProvider();

		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(http);

		var kit = new AuthKitService(accessor,
			resolver ?? Substitute.For<IAuthKitUserResolver>(),
			store,
			option,
			codes ?? Substitute.For<IOneTimeCodeStore>());

		return (kit, auth, http);
	}

	private static AuthKitService BuildWithoutHttpContext()
	{
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns((HttpContext?)null);

		return new AuthKitService(accessor,
			Substitute.For<IAuthKitUserResolver>(),
			null,
			new AuthKitOption(),
			Substitute.For<IOneTimeCodeStore>());
	}

	[Fact]
	public async Task PasswordSignIn_signs_in_resolved_user()
	{
		var resolver = Substitute.For<IAuthKitUserResolver>();
		resolver.ResolveAsync(Arg.Any<ExternalIdentity>()).Returns(new AuthKitUser("u1", "Alice"));
		var store = Substitute.For<IAuthKitPasswordStore>();
		store.ValidateAsync("bob", "secret").Returns(true);
		var (kit, auth, http) = Build(resolver: resolver, store: store);

		Assert.True(await kit.PasswordSignInAsync("bob", "secret"));

		await resolver.Received(1).ResolveAsync(
			Arg.Is<ExternalIdentity>(i => i.Provider == "password" && i.Id == "bob"));
		await auth.Received(1).SignInAsync(http, Scheme,
			Arg.Is<ClaimsPrincipal>(p => p.FindFirst(ClaimTypes.NameIdentifier)!.Value == "u1"
				&& p.FindFirst(ClaimTypes.Name)!.Value == "Alice"),
			Arg.Is<AuthenticationProperties>(p => p.IsPersistent
				&& p.ExpiresUtc > DateTimeOffset.UtcNow.AddDays(13.9)
				&& p.ExpiresUtc < DateTimeOffset.UtcNow.AddDays(14.1)));
	}

	[Fact]
	public async Task PasswordSignIn_rejects_wrong_password()
	{
		var store = Substitute.For<IAuthKitPasswordStore>();
		store.ValidateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
		var (kit, auth, _) = Build(store: store);

		Assert.False(await kit.PasswordSignInAsync("bob", "secret"));

		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task PasswordSignIn_rejects_when_provider_disabled()
	{
		var store = Substitute.For<IAuthKitPasswordStore>();
		store.ValidateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var (kit, auth, _) = Build(o => o.Password = null, store: store);

		Assert.False(await kit.PasswordSignInAsync("bob", "secret"));

		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task PasswordSignIn_rejects_when_no_password_store()
	{
		var (kit, auth, _) = Build();

		Assert.False(await kit.PasswordSignInAsync("bob", "secret"));

		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task PasswordSignIn_rejects_empty_login()
	{
		var store = Substitute.For<IAuthKitPasswordStore>();
		store.ValidateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var (kit, auth, _) = Build(store: store);

		Assert.False(await kit.PasswordSignInAsync("  ", "secret"));

		await store.DidNotReceiveWithAnyArgs().ValidateAsync(Arg.Any<string>(), Arg.Any<string>());
		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task PasswordSignIn_rejects_when_resolver_denies()
	{
		var store = Substitute.For<IAuthKitPasswordStore>();
		store.ValidateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var (kit, auth, _) = Build(store: store);

		Assert.False(await kit.PasswordSignInAsync("bob", "secret"));

		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task TelegramSignIn_signs_in_by_code()
	{
		var resolver = Substitute.For<IAuthKitUserResolver>();
		resolver.ResolveAsync(Arg.Any<ExternalIdentity>()).Returns(new AuthKitUser("u9"));
		var codes = Substitute.For<IOneTimeCodeStore>();
		codes.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "ABCD2345").Returns("42");
		var (kit, auth, http) = Build(o => o.Telegram = new TelegramOption { SessionLifetime = TimeSpan.FromDays(1) },
			resolver: resolver, codes: codes);

		Assert.True(await kit.TelegramSignInAsync("  ABCD2345  "));

		await resolver.Received(1).ResolveAsync(
			Arg.Is<ExternalIdentity>(i => i.Provider == "telegram" && i.Id == "42"));
		await auth.Received(1).SignInAsync(http, Scheme,
			Arg.Any<ClaimsPrincipal>(),
			Arg.Is<AuthenticationProperties>(p => p.ExpiresUtc > DateTimeOffset.UtcNow.AddHours(21)
				&& p.ExpiresUtc < DateTimeOffset.UtcNow.AddHours(25)));
	}

	[Fact]
	public async Task TelegramSignIn_rejects_unknown_code()
	{
		var codes = Substitute.For<IOneTimeCodeStore>();
		codes.TakeAsync(Arg.Any<OneTimeCodeChannel>(), Arg.Any<OneTimeCodePurpose>(), Arg.Any<string>()).Returns((string?)null);
		var resolver = Substitute.For<IAuthKitUserResolver>();
		var (kit, auth, _) = Build(resolver: resolver, codes: codes);

		Assert.False(await kit.TelegramSignInAsync("nope"));

		await resolver.DidNotReceiveWithAnyArgs().ResolveAsync(Arg.Any<ExternalIdentity>());
		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task TelegramSignIn_rejects_when_resolver_denies()
	{
		var codes = Substitute.For<IOneTimeCodeStore>();
		codes.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "ABCD2345").Returns("42");
		var resolver = Substitute.For<IAuthKitUserResolver>();
		var (kit, auth, _) = Build(resolver: resolver, codes: codes);

		Assert.False(await kit.TelegramSignInAsync("ABCD2345"));

		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task TelegramSignIn_uses_default_lifetime_when_provider_disabled()
	{
		var codes = Substitute.For<IOneTimeCodeStore>();
		codes.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "ABCD2345").Returns("7");
		var resolver = Substitute.For<IAuthKitUserResolver>();
		resolver.ResolveAsync(Arg.Any<ExternalIdentity>()).Returns(new AuthKitUser("u7"));
		var (kit, auth, _) = Build(o => o.Telegram = null, resolver: resolver, codes: codes);

		Assert.True(await kit.TelegramSignInAsync("ABCD2345"));

		await auth.Received(1).SignInAsync(Arg.Any<HttpContext>(), Scheme,
			Arg.Any<ClaimsPrincipal>(),
			Arg.Is<AuthenticationProperties>(p => p.ExpiresUtc > DateTimeOffset.UtcNow.AddDays(13.9)
				&& p.ExpiresUtc < DateTimeOffset.UtcNow.AddDays(14.1)));
	}

	[Fact]
	public async Task IssueTelegramCode_passes_user_and_lifetime()
	{
		var codes = Substitute.For<IOneTimeCodeStore>();
		var (kit, _, _) = Build(o => o.Telegram = new TelegramOption { CodeLifetime = TimeSpan.FromMinutes(5) },
			codes: codes);

		await kit.IssueTelegramCodeAsync(777);

		await codes.Received(1).IssueAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "777", TimeSpan.FromMinutes(5));
	}

	[Fact]
	public async Task IssueTelegramCode_throws_when_provider_disabled()
	{
		var codes = Substitute.For<IOneTimeCodeStore>();
		var (kit, _, _) = Build(o => o.Telegram = null, codes: codes);

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => kit.IssueTelegramCodeAsync(777));
		Assert.Contains("disabled", ex.Message);
		await codes.DidNotReceiveWithAnyArgs().IssueAsync(Arg.Any<OneTimeCodeChannel>(), Arg.Any<OneTimeCodePurpose>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
	}

	[Fact]
	public async Task SignInAsync_sets_claims_without_expiry_by_default()
	{
		var (kit, auth, http) = Build();

		await kit.SignInAsync(new AuthKitUser("u2"));

		await auth.Received(1).SignInAsync(http, Scheme,
			Arg.Is<ClaimsPrincipal>(p => p.FindFirst(ClaimTypes.NameIdentifier)!.Value == "u2"
				&& p.FindFirst(ClaimTypes.Name) == null),
			Arg.Is<AuthenticationProperties>(p => p.IsPersistent && p.ExpiresUtc == null));
	}

	[Fact]
	public async Task SignInAsync_appends_resolver_claims()
	{
		var (kit, auth, http) = Build();

		await kit.SignInAsync(new AuthKitUser("u3", "Alice",
		[
			new Claim(ClaimTypes.Role, "admin"),
			new Claim("custom", "s1"),
		]));

		await auth.Received(1).SignInAsync(http, Scheme,
			Arg.Is<ClaimsPrincipal>(p => p.FindFirst(ClaimTypes.NameIdentifier)!.Value == "u3"
				&& p.FindFirst(ClaimTypes.Name)!.Value == "Alice"
				&& p.FindFirst(ClaimTypes.Role)!.Value == "admin"
				&& p.FindFirst("custom")!.Value == "s1"),
			Arg.Is<AuthenticationProperties>(p => p.IsPersistent && p.ExpiresUtc == null));
	}

	[Fact]
	public async Task SignOut_calls_the_authentication_handler()
	{
		var (kit, auth, http) = Build();

		await kit.SignOutAsync();

		await auth.Received(1).SignOutAsync(http, Scheme, Arg.Any<AuthenticationProperties?>());
	}

	[Fact]
	public async Task ChallengeGoogle_keeps_local_return_path()
	{
		var (kit, auth, http) = Build();

		await kit.ChallengeGoogleAsync("/back");

		await auth.Received(1).ChallengeAsync(http, AuthKitService.GoogleScheme,
			Arg.Is<AuthenticationProperties>(p => p.RedirectUri == "/back"));
	}

	[Fact]
	public async Task ChallengeGoogle_falls_back_for_non_local_url()
	{
		var (kit, auth, http) = Build(o => o.DefaultReturnPath = "/home");

		await kit.ChallengeGoogleAsync("https://evil.example/steal");

		await auth.Received(1).ChallengeAsync(http, AuthKitService.GoogleScheme,
			Arg.Is<AuthenticationProperties>(p => p.RedirectUri == "/home"));
	}

	[Fact]
	public async Task ChallengeGoogle_falls_back_to_root_by_default()
	{
		var (kit, auth, http) = Build();

		await kit.ChallengeGoogleAsync("//evil.example");

		await auth.Received(1).ChallengeAsync(http, AuthKitService.GoogleScheme,
			Arg.Is<AuthenticationProperties>(p => p.RedirectUri == "/"));
	}

	[Fact]
	public async Task ChallengeGoogle_throws_without_http_context()
	{
		var kit = BuildWithoutHttpContext();

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => kit.ChallengeGoogleAsync("/back"));
		Assert.Contains("http context", ex.Message);
	}

	[Fact]
	public async Task SignInAsync_throws_without_http_context()
	{
		var kit = BuildWithoutHttpContext();

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => kit.SignInAsync(new AuthKitUser("u2")));
		Assert.Contains("http context", ex.Message);
	}

	[Fact]
	public async Task SignOutAsync_throws_without_http_context()
	{
		var kit = BuildWithoutHttpContext();

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => kit.SignOutAsync());
		Assert.Contains("http context", ex.Message);
	}

	[Fact]
	public async Task ResolveExternalSignIn_signs_in_resolved_user()
	{
		var resolver = Substitute.For<IAuthKitUserResolver>();
		resolver.ResolveAsync(Arg.Any<ExternalIdentity>()).Returns(new AuthKitUser("g1", "Google Guy"));
		var (kit, auth, _) = Build(resolver: resolver);

		Assert.True(await kit.ResolveExternalSignInAsync(
			new ExternalIdentity("google", "ext-1", "guy@example.com", "Google Guy"), TimeSpan.FromDays(2)));

		await auth.Received(1).SignInAsync(Arg.Any<HttpContext>(), Scheme,
			Arg.Is<ClaimsPrincipal>(p => p.FindFirst(ClaimTypes.NameIdentifier)!.Value == "g1"),
			Arg.Is<AuthenticationProperties>(p => p.ExpiresUtc > DateTimeOffset.UtcNow.AddDays(1.9)
				&& p.ExpiresUtc < DateTimeOffset.UtcNow.AddDays(2.1)));
	}

	[Fact]
	public async Task ResolveExternalSignIn_rejects_when_resolver_denies()
	{
		var (kit, auth, _) = Build();

		Assert.False(await kit.ResolveExternalSignInAsync(new ExternalIdentity("google", "ext-1"), TimeSpan.FromDays(2)));

		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public void MapGoogleTicket_reads_claims()
	{
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
		[
			new Claim(ClaimTypes.NameIdentifier, "google-123"),
			new Claim(ClaimTypes.Email, "guy@gmail.com"),
			new Claim(ClaimTypes.Name, "Guy"),
		]));

		var identity = AuthKitService.MapGoogleTicket(principal);

		Assert.NotNull(identity);
		Assert.Equal(new ExternalIdentity("google", "google-123", "guy@gmail.com", "Guy"), identity);
	}

	[Fact]
	public void MapGoogleTicket_returns_null_without_name_identifier()
	{
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
			new[] { new Claim(ClaimTypes.Email, "guy@gmail.com") }));

		Assert.Null(AuthKitService.MapGoogleTicket(principal));
		Assert.Null(AuthKitService.MapGoogleTicket(null));
	}

	[Theory]
	[InlineData("/", true)]
	[InlineData("/back", true)]
	[InlineData("~/back", true)]
	[InlineData("", false)]
	[InlineData("~", false)]
	[InlineData("//evil.example", false)]
	[InlineData("/\\evil", false)]
	[InlineData("https://evil.example", false)]
	[InlineData("~back", false)]
	[InlineData("back", false)]
	public void IsLocalUrl_accepts_only_app_relative_paths(string url, bool expected)
	{
		Assert.Equal(expected, AuthKitService.IsLocalUrl(url));
	}
}
