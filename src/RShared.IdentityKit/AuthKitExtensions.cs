using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RShared.IdentityKit;

/// <summary>
/// Dependency injection and pipeline extensions
/// </summary>
public static class AuthKitExtensions
{
	/// <summary>
	/// Adds AuthKit: a cookie session scheme, enabled providers and the consumer seams.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configure">Configure AuthKit providers and paths</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddAuthKit(this IServiceCollection services, Action<AuthKitOption> configure)
	{
		var option = new AuthKitOption();
		configure(option);
		return services.AddAuthKitCore(option);
	}

	/// <summary>
	/// Adds AuthKit bound to a configuration section ("authkit" by default).
	/// Providers are enabled by section presence: an empty or missing section disables the provider.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="section">Configuration section</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddAuthKit(this IServiceCollection services, IConfigurationSection section)
	{
		var option = new AuthKitOption();
		section.Bind(option);

		// провайдеры включаются присутствием секции, пустая секция провайдера не должна его включать
		option.Password = section.GetSection("password").Exists() ? option.Password : null;
		option.Telegram = section.GetSection("telegram").Exists() ? option.Telegram : null;

		var googleSection = section.GetSection("google");
		if (googleSection.Exists() && !string.IsNullOrWhiteSpace(googleSection["clientId"]))
		{
			option.Google = new GoogleOption();
			googleSection.Bind(option.Google);
		}
		else
		{
			option.Google = null;
		}

		return services.AddAuthKitCore(option);
	}

	/// <summary>
	/// Adds AuthKit to a web application builder.
	/// </summary>
	public static WebApplicationBuilder AddAuthKit(this WebApplicationBuilder builder, Action<AuthKitOption> configure)
	{
		AddAuthKit(builder.Services, configure);
		return builder;
	}

	/// <summary>
	/// Adds AuthKit bound to a configuration section to a web application builder.
	/// </summary>
	public static WebApplicationBuilder AddAuthKit(this WebApplicationBuilder builder, IConfigurationSection section)
	{
		AddAuthKit(builder.Services, section);
		return builder;
	}

	/// <summary>
	/// Maps Razor pages including the built-in login page. Call instead of <c>MapRazorPages</c>.
	/// </summary>
	public static WebApplication MapAuthKit(this WebApplication app)
	{
		// Stryker disable once Statement : маппинг страницы логина, в юнит-хосте без ApplicationParts endpoints пустые
		app.MapRazorPages();
		return app;
	}

	private static IServiceCollection AddAuthKitCore(this IServiceCollection services, AuthKitOption option)
	{
		if (option.Google is { } google
			&& (string.IsNullOrWhiteSpace(google.ClientId) || string.IsNullOrWhiteSpace(google.ClientSecret)))
		{
			throw new InvalidOperationException("AuthKit: google provider is enabled, but clientId/clientSecret are empty");
		}

		services.AddHttpContextAccessor();

		// built-in login pages need the Razor Pages runtime; the call is additive,
		// a consumer configuring Razor Pages itself is free to do it again
		// Stryker disable once Statement : фреймворковая регистрация, эффект виден только при рендере страницы в реальном хосте
		services.AddRazorPages();

		services.AddAuthentication(option.Scheme)
			.AddCookie(option.Scheme, cookie =>
			{
				cookie.LoginPath = option.LoginPath;
				cookie.Cookie.Name = option.Scheme;
				cookie.ExpireTimeSpan = option.Password?.SessionLifetime
					?? option.Telegram?.SessionLifetime
					?? TimeSpan.FromDays(14);
			});

		if (option.Google is { } googleOptions)
		{
			services.AddAuthentication()
				.AddGoogle(AuthKitService.GoogleScheme, o =>
				{
					o.ClientId = googleOptions.ClientId;
					o.ClientSecret = googleOptions.ClientSecret;
					o.CallbackPath = googleOptions.CallbackPath;
					o.Events.OnTicketReceived = async context =>
					{
						// a proven Google identity → the consumer resolver → a session in the main scheme
						var identity = AuthKitService.MapGoogleTicket(context.Principal);
						if (identity is null)
						{
							context.Fail("AuthKit: the Google ticket has no name identifier claim");
							return;
						}

						var kit = (AuthKitService)context.HttpContext.RequestServices.GetRequiredService<IAuthKit>();
						if (!await kit.ResolveExternalSignInAsync(identity, googleOptions.SessionLifetime))
						{
							context.Fail("AuthKit: the Google identity is not allowed to sign in");
							return;
						}

						context.Success();
					};
				});
		}

		services.TryAddSingleton(option);
		services.TryAddSingleton<IOneTimeCodeStore, MemoryOneTimeCodeStore>();
		services.TryAddScoped<IAuthKit>(sp => new AuthKitService(
			sp.GetRequiredService<IHttpContextAccessor>(),
			sp.GetRequiredService<IAuthKitUserResolver>(),
			sp.GetService<IAuthKitPasswordStore>(),
			sp.GetRequiredService<AuthKitOption>(),
			sp.GetRequiredService<IOneTimeCodeStore>()));

		return services;
	}
}
