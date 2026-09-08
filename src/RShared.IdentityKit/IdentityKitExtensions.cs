using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RShared.IdentityKit;
using RShared.Orm;

namespace RShared.IdentityKit;

/// <summary>
/// Dependency injection extensions.
/// </summary>
public static class IdentityKitExtensions
{
	/// <summary>
	/// Adds IdentityKit: EF implementations of the AuthKit seams, the user service
	/// and security stamp validation of AuthKit sessions. Requires AddAuthKit and
	/// Orm repositories (the consumer context must map the entities with
	/// <see cref="ModelBuilderExtensions.ApplyIdentityKit{TUser}"/>).
	/// </summary>
	/// <typeparam name="TUser">User entity mapped in the consumer context</typeparam>
	/// <param name="services">Service collection</param>
	/// <param name="configure">Configure IdentityKit</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddIdentityKit<TUser>(this IServiceCollection services, Action<IdentityKitOption> configure)
		where TUser : IdentityKitUser, new()
	{
		var option = BuildOption(configure);

		services.TryAddSingleton(option);
		services.TryAddSingleton<IPasswordHasher<TUser>, PasswordHasher<TUser>>();
		services.TryAddScoped<IAuthKitPasswordStore, EfPasswordStore<TUser>>();
		services.TryAddScoped<IAuthKitUserResolver, EfUserResolver<TUser>>();

		// дефолт AuthKit (память процесса) не годится, когда юзеры и коды живут в базе:
		// снимаем именно его, свою реализацию потребитель по-прежнему регистрирует до вызова
		var authKitDefault = services.FirstOrDefault(d =>
			d.ServiceType == typeof(IOneTimeCodeStore) && d.ImplementationType == typeof(MemoryOneTimeCodeStore));
		if (authKitDefault is not null)
		{
			services.Remove(authKitDefault);
		}

		services.TryAddScoped<IOneTimeCodeStore, EfOneTimeCodeStore>();
		services.TryAddScoped<SecurityStampValidator<TUser>>();
		services.TryAddScoped<IIdentityKit>(sp => new IdentityKitService<TUser>(
			sp.GetRequiredService<IAuthKit>(),
			sp.GetRequiredService<IEntityRepositoryFactory>(),
			sp.GetRequiredService<IPasswordHasher<TUser>>(),
			sp.GetService<IEmailSender>(),
			sp.GetRequiredService<IOneTimeCodeStore>(),
			sp.GetRequiredService<IdentityKitOption>()));

		// Stryker disable once Statement : IMemoryCache транзитивно приходит из AddRazorPages хоста AuthKit, вызов — явная страховка
		services.AddMemoryCache();

		// подписка на cookie-схему AuthKit: пост-конфигурация исполняется лениво
		// при первом options.Get(scheme), порядок AddAuthKit/AddIdentityKit неважен
		services.PostConfigure<CookieAuthenticationOptions>(option.SessionScheme, cookie =>
		{
			var previous = cookie.Events.OnValidatePrincipal;
			cookie.Events.OnValidatePrincipal = async context =>
			{
				await context.HttpContext.RequestServices
					.GetRequiredService<SecurityStampValidator<TUser>>()
					.ValidateAsync(context);

				// чужой валидатор, если потребитель повесил свой, зовём после нашего
				if (previous is not null)
				{
					await previous(context);
				}
			};
		});

		return services;
	}

	/// <summary>
	/// Adds IdentityKit to a web application builder.
	/// </summary>
	public static WebApplicationBuilder AddIdentityKit<TUser>(this WebApplicationBuilder builder, Action<IdentityKitOption> configure)
		where TUser : IdentityKitUser, new()
	{
		AddIdentityKit<TUser>(builder.Services, configure);
		return builder;
	}

	private static IdentityKitOption BuildOption(Action<IdentityKitOption> configure)
	{
		var option = new IdentityKitOption();
		configure(option);

		if (string.IsNullOrWhiteSpace(option.CodeHashPepper))
		{
			throw new ArgumentException("CodeHashPepper is required", nameof(configure));
		}

		if (option.CodeLifetime <= TimeSpan.Zero)
		{
			throw new ArgumentException("CodeLifetime must be positive", nameof(configure));
		}

		if (option.SessionLifetime <= TimeSpan.Zero)
		{
			throw new ArgumentException("SessionLifetime must be positive", nameof(configure));
		}

		if (option.SecurityStampValidationInterval <= TimeSpan.Zero)
		{
			throw new ArgumentException("SecurityStampValidationInterval must be positive", nameof(configure));
		}

		return option;
	}
}
