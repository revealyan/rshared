using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RShared.IdentityKit;

namespace RShared.Redis.IdentityKit;

/// <summary>
/// Redis-backed seams for IdentityKit.
/// </summary>
public static class RedisIdentityKitExtensions
{
	/// <summary>
	/// One time codes and the security stamp cache in Redis: multi-node friendly —
	/// codes are shared by every node, session invalidation is instant and global.
	/// Call after AddRedis and before AddAuthKit/AddIdentityKit: the TryAdd defaults
	/// of both must not land first.
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <returns>Service collection</returns>
	public static IServiceCollection AddRedisIdentityKit(this IServiceCollection services)
	{
		// Stryker disable once Equality : инверсия guard на пустой коллекции даёт то же сообщение
		if (services.All(d => d.ServiceType != typeof(IRedisCache)))
		{
			throw new ArgumentException("Call AddRedis before AddRedisIdentityKit");
		}

		services.TryAddSingleton<IOneTimeCodeStore, RedisOneTimeCodeStore>();
		services.TryAddSingleton<ISecurityStampCache, RedisSecurityStampCache>();
		return services;
	}

	/// <summary>
	/// Adds Redis-backed IdentityKit seams to a web application builder.
	/// </summary>
	public static WebApplicationBuilder AddRedisIdentityKit(this WebApplicationBuilder builder)
	{
		AddRedisIdentityKit(builder.Services);
		return builder;
	}
}
