using Xunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RShared.AuthKit;
using RShared.IdentityKit;
using RShared.Orm;
using RShared.Orm.EntityFrameworkCore;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Регистрации: дефолты option, валидация BuildOption, TryAdd-перебиваемость,
/// подмена дефолтного стора кодов AuthKit, крючок валидатора в cookie-схему
/// </summary>
public sealed class RegistrationTests
{
	[Fact]
	public void Default_option_values()
	{
		var option = new IdentityKitOption();

		Assert.Equal(AuthKitOption.DefaultScheme, option.SessionScheme);
		Assert.Equal(string.Empty, option.CodeHashPepper);
		Assert.Equal(TimeSpan.FromMinutes(10), option.CodeLifetime);
		Assert.Equal(TimeSpan.FromDays(14), option.SessionLifetime);
		Assert.Equal(TimeSpan.FromMinutes(30), option.SecurityStampValidationInterval);
		Assert.True(option.CreateUsersOnFirstExternalSignIn);
		Assert.False(option.LinkByEmail);
	}

	[Fact]
	public void Empty_pepper_throws()
	{
		var ex = Assert.Throws<ArgumentException>(() =>
			new ServiceCollection().AddIdentityKit<IdentityKitUser>(_ => { }));

		Assert.Contains("CodeHashPepper", ex.Message);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void Nonpositive_code_lifetime_throws(int seconds)
	{
		var ex = Assert.Throws<ArgumentException>(() =>
			new ServiceCollection().AddIdentityKit<IdentityKitUser>(o =>
			{
				o.CodeHashPepper = "p";
				o.CodeLifetime = TimeSpan.FromSeconds(seconds);
			}));

		Assert.Contains("CodeLifetime", ex.Message);
	}

	[Fact]
	public void Nonpositive_validation_interval_throws()
	{
		var ex = Assert.Throws<ArgumentException>(() =>
			new ServiceCollection().AddIdentityKit<IdentityKitUser>(o =>
			{
				o.CodeHashPepper = "p";
				o.SecurityStampValidationInterval = TimeSpan.Zero;
			}));

		Assert.Contains("SecurityStampValidationInterval", ex.Message);
	}

	[Fact]
	public void Nonpositive_session_lifetime_throws()
	{
		var ex = Assert.Throws<ArgumentException>(() =>
			new ServiceCollection().AddIdentityKit<IdentityKitUser>(o =>
			{
				o.CodeHashPepper = "p";
				o.SessionLifetime = TimeSpan.Zero;
			}));

		Assert.Contains("SessionLifetime", ex.Message);
	}

	[Fact]
	public void Default_seams_resolve()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		Assert.IsType<EfPasswordStore<IdentityKitUser>>(scope.Get<IAuthKitPasswordStore>());
		Assert.IsType<EfUserResolver<IdentityKitUser>>(scope.Get<IAuthKitUserResolver>());
		Assert.IsType<EfOneTimeCodeStore>(scope.Get<IOneTimeCodeStore>());
		Assert.NotNull(scope.Get<IIdentityKit>());

		// валидатор стемпа резолвится: регистрация и AddMemoryCache на месте
		Assert.NotNull(scope.Get<SecurityStampValidator<IdentityKitUser>>());
	}

	[Fact]
	public void Authkit_memory_code_store_is_replaced_by_the_ef_one()
	{
		// порядок потребителя: AddAuthKit до AddIdentityKit — дефолт памяти должен уступить EF-стору
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		var codes = scope.Get<IOneTimeCodeStore>();
		Assert.IsType<EfOneTimeCodeStore>(codes);
	}

	[Fact]
	public void Consumer_code_store_is_not_replaced()
	{
		var codes = Substitute.For<IOneTimeCodeStore>();
		var services = new ServiceCollection();
		services.AddSingleton(codes);
		services.AddAuthKit(_ => { });
		services.AddIdentityKit<IdentityKitUser>(o => o.CodeHashPepper = "p");

		var provider = services.BuildServiceProvider();
		Assert.Same(codes, provider.GetRequiredService<IOneTimeCodeStore>());
	}

	[Fact]
	public void Consumer_password_store_is_not_replaced()
	{
		var store = Substitute.For<IAuthKitPasswordStore>();
		var services = new ServiceCollection();
		services.AddScoped(_ => store);
		services.AddAuthKit(_ => { });
		services.AddIdentityKit<IdentityKitUser>(o => o.CodeHashPepper = "p");

		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		Assert.Same(store, scope.ServiceProvider.GetRequiredService<IAuthKitPasswordStore>());
	}

	[Fact]
	public async Task PostConfigure_wires_a_working_validator_into_the_authkit_scheme()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		var cookie = fixture.Provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
			.Get(AuthKitOption.DefaultScheme);
		Assert.NotNull(cookie.Events.OnValidatePrincipal);

		// делегат реально валидирует: юзера нет — сессия режется, а не молчит заглушкой
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
		[
			new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
			new Claim(IdentityKitClaims.SecurityStamp, "s1"),
		]));
		var scheme = new AuthenticationScheme(AuthKitOption.DefaultScheme, null, typeof(CookieAuthenticationHandler));
		var http = new DefaultHttpContext { RequestServices = scope.Services };
		var context = new CookieValidatePrincipalContext(http, scheme, cookie, new AuthenticationTicket(principal, scheme.Name));

		await cookie.Events.OnValidatePrincipal(context);

		Assert.Null(context.Principal);
	}

	[Fact]
	public async Task Consumer_validate_principal_delegate_stays_in_the_chain()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		var services = new ServiceCollection()
			.AddEntityRepositories(typeof(KitContext));
		services.AddDbContext<KitContext>(options => options.UseSqlite(connection));
		services.AddScoped(_ => Substitute.For<IAuthKitUserResolver>());
		services.AddAuthKit(_ => { });

		// свой валидатор потребителя, повешенный ДО AddIdentityKit, должен зваться после нашего
		var consumerCalled = false;
		services.PostConfigure<CookieAuthenticationOptions>(AuthKitOption.DefaultScheme, o =>
			o.Events.OnValidatePrincipal = _ => { consumerCalled = true; return Task.CompletedTask; });
		services.AddIdentityKit<IdentityKitUser>(o => o.CodeHashPepper = "p");

		using var provider = services.BuildServiceProvider();
		using (provider.CreateScope())
		{
			var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
				.Get(AuthKitOption.DefaultScheme);

			var principal = new ClaimsPrincipal(new ClaimsIdentity(
				new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }));
			var scheme = new AuthenticationScheme(AuthKitOption.DefaultScheme, null, typeof(CookieAuthenticationHandler));
			var http = new DefaultHttpContext { RequestServices = provider.CreateScope().ServiceProvider };
			var context = new CookieValidatePrincipalContext(http, scheme, cookie, new AuthenticationTicket(principal, scheme.Name));

			await cookie.Events.OnValidatePrincipal(context);

			Assert.True(consumerCalled);
		}
	}

	[Fact]
	public void WebApplication_builder_overload_registers_identitykit()
	{
		var builder = WebApplication.CreateBuilder();
		builder.AddIdentityKit<IdentityKitUser>(o => o.CodeHashPepper = "pepper");

		using var app = builder.Build();

		Assert.Equal("pepper", app.Services.GetRequiredService<IdentityKitOption>().CodeHashPepper);
	}
}
