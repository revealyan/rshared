using Xunit;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RShared.AuthKit;

namespace RShared.AuthKit.Tests;

/// <summary>
/// Регистрации и биндинг конфига: провайдеры включаются содержимым секций
/// </summary>
public sealed class ExtensionsTests
{
	private static IConfigurationSection Section(string json)
	{
		var config = new ConfigurationBuilder()
			.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
			.Build();
		return config.GetSection("authkit");
	}

	[Fact]
	public void Config_sections_enable_and_bind_providers()
	{
		var services = new ServiceCollection();
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());

		services.AddAuthKit(Section("""
			{ "authkit": {
				"password": { "sessionLifetime": "1.00:00:00" },
				"telegram": { "codeLifetime": "00:05:00" },
				"google": { "clientId": "cid", "clientSecret": "secret" }
			} }
			"""));

		var option = services.BuildServiceProvider().GetRequiredService<AuthKitOption>();
		Assert.NotNull(option.Password);
		Assert.Equal(TimeSpan.FromDays(1), option.Password.SessionLifetime);
		Assert.NotNull(option.Telegram);
		Assert.Equal(TimeSpan.FromMinutes(5), option.Telegram.CodeLifetime);
		Assert.NotNull(option.Google);
		Assert.Equal("cid", option.Google.ClientId);
		Assert.Equal("secret", option.Google.ClientSecret);
	}

	[Fact]
	public void Missing_sections_disable_providers()
	{
		var services = new ServiceCollection();
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());

		services.AddAuthKit(Section("""{ "authkit": { "loginPageTitle": "Sign in" } }"""));

		var option = services.BuildServiceProvider().GetRequiredService<AuthKitOption>();
		Assert.Null(option.Password);
		Assert.Null(option.Telegram);
		Assert.Null(option.Google);
		Assert.Equal("Sign in", option.LoginPageTitle);
	}

	[Fact]
	public void Empty_password_section_disables_password()
	{
		var services = new ServiceCollection();
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());

		services.AddAuthKit(Section("""{ "authkit": { "password": {} } }"""));

		var option = services.BuildServiceProvider().GetRequiredService<AuthKitOption>();
		Assert.Null(option.Password);
	}

	[Fact]
	public void Google_without_client_id_is_disabled()
	{
		var services = new ServiceCollection();
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());

		services.AddAuthKit(Section("""{ "authkit": { "google": { "clientSecret": "secret" } } }"""));

		var option = services.BuildServiceProvider().GetRequiredService<AuthKitOption>();
		Assert.Null(option.Google);
	}

	[Fact]
	public void Enabled_google_without_credentials_throws()
	{
		var services = new ServiceCollection();

		var ex = Assert.Throws<InvalidOperationException>(() => services.AddAuthKit(o =>
			o.Google = new GoogleOption()));
		Assert.Contains("empty", ex.Message);
	}

	[Fact]
	public void Default_option_values()
	{
		var option = new AuthKitOption();

		Assert.Equal("RShared.AuthKit", option.Scheme);
		Assert.Equal("/authkit/login", option.LoginPath);
		Assert.Equal("/", option.DefaultReturnPath);
		Assert.Equal("Sign in", option.LoginPageTitle);
		Assert.Equal(TimeSpan.FromDays(14), option.Password!.SessionLifetime);
		Assert.Null(option.Google);
		Assert.Equal(TimeSpan.FromMinutes(10), option.Telegram!.CodeLifetime);
		Assert.Equal(TimeSpan.FromDays(14), option.Telegram.SessionLifetime);

		var google = new GoogleOption();
		Assert.Equal(string.Empty, google.ClientId);
		Assert.Equal(string.Empty, google.ClientSecret);
		Assert.Equal("/authkit/google-callback", google.CallbackPath);
		Assert.Equal(TimeSpan.FromDays(14), google.SessionLifetime);
	}

	private static CookieAuthenticationOptions CookieOptions(IServiceCollection services)
	{
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());
		return services.BuildServiceProvider()
			.GetRequiredService<IOptionsFactory<CookieAuthenticationOptions>>()
			.Create("RShared.AuthKit");
	}

	[Fact]
	public void Cookie_scheme_points_to_the_login_page()
	{
		var cookie = CookieOptions(new ServiceCollection()
			.AddAuthKit(_ => { }));

		Assert.Equal("/authkit/login", cookie.LoginPath);
		Assert.Equal("RShared.AuthKit", cookie.Cookie.Name);
	}

	[Fact]
	public void Cookie_expiry_follows_the_password_lifetime()
	{
		var cookie = CookieOptions(new ServiceCollection()
			.AddAuthKit(o => o.Password = new PasswordOption { SessionLifetime = TimeSpan.FromDays(3) }));

		Assert.Equal(TimeSpan.FromDays(3), cookie.ExpireTimeSpan);
	}

	[Fact]
	public void Cookie_expiry_falls_back_to_telegram_lifetime()
	{
		var cookie = CookieOptions(new ServiceCollection()
			.AddAuthKit(o =>
			{
				o.Password = null;
				o.Telegram = new TelegramOption { SessionLifetime = TimeSpan.FromDays(2) };
			}));

		Assert.Equal(TimeSpan.FromDays(2), cookie.ExpireTimeSpan);
	}

	[Fact]
	public void Cookie_expiry_defaults_to_two_weeks_without_local_providers()
	{
		var cookie = CookieOptions(new ServiceCollection()
			.AddAuthKit(o =>
			{
				o.Password = null;
				o.Telegram = null;
				o.Google = new GoogleOption { ClientId = "cid", ClientSecret = "secret" };
			}));

		Assert.Equal(TimeSpan.FromDays(14), cookie.ExpireTimeSpan);
	}

	private static async Task<TicketReceivedContext> RunTicketReceived(
		ClaimsPrincipal principal, IAuthKitUserResolver resolver, IAuthenticationService auth)
	{
		var services = new ServiceCollection();
		services.AddScoped(_ => resolver);
		// AuthKitService берёт контекст из accessor; AddAuthKit не должен его перебить
		var accessor = Substitute.For<IHttpContextAccessor>();
		services.AddSingleton(accessor);
		services.AddAuthKit(o => o.Google = new GoogleOption { ClientId = "cid", ClientSecret = "secret" });
		// внешний вход пишет сессию через IAuthenticationService: подменяем на фейк
		services.Replace(ServiceDescriptor.Singleton(auth));

		var provider = services.BuildServiceProvider();
		var google = provider.GetRequiredService<IOptionsFactory<GoogleOptions>>().Create("RShared.AuthKit.Google");
		Assert.NotNull(google.Events.OnTicketReceived);

		var http = new DefaultHttpContext { RequestServices = provider };
		accessor.HttpContext.Returns(http);
		var scheme = new AuthenticationScheme("RShared.AuthKit.Google", null, typeof(GoogleHandler));
		var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), scheme.Name);
		var context = new TicketReceivedContext(http, scheme, google, ticket);
		await google.Events.OnTicketReceived(context);
		return context;
	}

	[Fact]
	public async Task Google_ticket_without_name_identifier_fails()
	{
		var auth = Substitute.For<IAuthenticationService>();
		var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "guy@gmail.com")]));

		var context = await RunTicketReceived(principal, Substitute.For<IAuthKitUserResolver>(), auth);

		Assert.False(context.Result!.Succeeded);
		Assert.Contains("name identifier", context.Result.Failure!.Message);
		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(),
			Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task Google_ticket_signs_in_the_resolved_user()
	{
		var auth = Substitute.For<IAuthenticationService>();
		var resolver = Substitute.For<IAuthKitUserResolver>();
		resolver.ResolveAsync(Arg.Any<ExternalIdentity>()).Returns(new AuthKitUser("g1", "Guy"));
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
		[
			new Claim(ClaimTypes.NameIdentifier, "google-123"),
			new Claim(ClaimTypes.Email, "guy@gmail.com"),
			new Claim(ClaimTypes.Name, "Guy"),
		]));

		var context = await RunTicketReceived(principal, resolver, auth);

		Assert.True(context.Result!.Succeeded);
		await resolver.Received(1).ResolveAsync(Arg.Is<ExternalIdentity>(i =>
			i.Provider == "google" && i.Id == "google-123" && i.Email == "guy@gmail.com" && i.Name == "Guy"));
		await auth.Received(1).SignInAsync(Arg.Any<HttpContext>(), "RShared.AuthKit",
			Arg.Any<ClaimsPrincipal>(),
			Arg.Is<AuthenticationProperties>(p => p.ExpiresUtc > DateTimeOffset.UtcNow.AddDays(13.9)
				&& p.ExpiresUtc < DateTimeOffset.UtcNow.AddDays(14.1)));
	}

	[Fact]
	public async Task Google_ticket_fails_when_resolver_denies()
	{
		var auth = Substitute.For<IAuthenticationService>();
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, "google-123")]));

		var context = await RunTicketReceived(principal, Substitute.For<IAuthKitUserResolver>(), auth);

		Assert.False(context.Result!.Succeeded);
		Assert.Contains("not allowed", context.Result.Failure!.Message);
		await auth.DidNotReceiveWithAnyArgs().SignInAsync(Arg.Any<HttpContext>(), Arg.Any<string>(),
			Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public void WebApplication_configure_overload_registers_authkit()
	{
		var builder = WebApplication.CreateBuilder();
		builder.Services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());
		builder.AddAuthKit(o => o.LoginPageTitle = "Entry");

		var app = builder.Build();

		Assert.Equal("Entry", app.Services.GetRequiredService<AuthKitOption>().LoginPageTitle);
	}

	[Fact]
	public void WebApplication_section_overload_registers_and_maps_authkit()
	{
		var builder = WebApplication.CreateBuilder();
		builder.Services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());
		builder.AddAuthKit(Section("""{ "authkit": { "loginPageTitle": "Entry" } }"""));

		var app = builder.Build();
		Assert.Same(app, app.MapAuthKit());

		Assert.Equal("Entry", app.Services.GetRequiredService<AuthKitOption>().LoginPageTitle);
	}

	[Fact]
	public async Task Registers_cookie_and_google_schemes()
	{
		var services = new ServiceCollection();
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());

		services.AddAuthKit(o => o.Google = new GoogleOption { ClientId = "cid", ClientSecret = "secret" });

		var provider = services.BuildServiceProvider();
		var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
		Assert.Contains(schemes, s => s.Name == "RShared.AuthKit");
		Assert.Contains(schemes, s => s.Name == "RShared.AuthKit.Google");
	}

	[Fact]
	public async Task Registers_only_cookie_scheme_without_google()
	{
		var services = new ServiceCollection();
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());

		services.AddAuthKit(_ => { });

		var provider = services.BuildServiceProvider();
		var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
		Assert.Equal(["RShared.AuthKit"], schemes.Select(s => s.Name).ToArray());
	}

	[Fact]
	public void Registers_default_seams()
	{
		var services = new ServiceCollection();
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());

		services.AddAuthKit(_ => { });

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		Assert.IsType<MemoryTgCodeStore>(scope.ServiceProvider.GetRequiredService<ITgCodeStore>());
		Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthKit>());
	}

	[Fact]
	public void Consumer_code_store_is_not_replaced()
	{
		var codes = Substitute.For<ITgCodeStore>();
		var services = new ServiceCollection();
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());
		services.AddSingleton(codes);

		services.AddAuthKit(_ => { });

		// регистрация ДО AddAuthKit: TryAdd не должен перебивать её
		var provider = services.BuildServiceProvider();
		Assert.Same(codes, provider.GetRequiredService<ITgCodeStore>());
	}
}
