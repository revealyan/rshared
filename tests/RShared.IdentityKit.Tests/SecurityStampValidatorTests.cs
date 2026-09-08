using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using RShared.IdentityKit;
using RShared.Orm;

using Xunit;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Валидатор сессий:Reject при изменённом stamp/блокировке/удалении,
/// пропуск сессий без stamp-клейма, кэш в пределах интервала
/// </summary>
public sealed class SecurityStampValidatorTests
{
	private readonly IEntityRepository<IdentityKitUser> _repo = Substitute.For<IEntityRepository<IdentityKitUser>>();
	private readonly IEntityRepositoryFactory _factory = Substitute.For<IEntityRepositoryFactory>();
	private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

	public SecurityStampValidatorTests()
	{
		_factory.Create<IdentityKitUser>().Returns(_repo);
	}

	private SecurityStampValidator<IdentityKitUser> Validator(IdentityKitOption? option = null)
	{
		return new SecurityStampValidator<IdentityKitUser>(
			_factory,
			_cache,
			option ?? new IdentityKitOption { CodeHashPepper = "p", SecurityStampValidationInterval = TimeSpan.FromMilliseconds(1) });
	}

	private static CookieValidatePrincipalContext Context(ClaimsPrincipal principal)
	{
		var scheme = new AuthenticationScheme("RShared.AuthKit", null, typeof(CookieAuthenticationHandler));
		var ticket = new AuthenticationTicket(principal, scheme.Name);
		return new CookieValidatePrincipalContext(new DefaultHttpContext(), scheme, new CookieAuthenticationOptions(), ticket);
	}

	private static ClaimsPrincipal Principal(Guid userId, string? stamp = "s1")
	{
		var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
		if (stamp is not null)
		{
			claims.Add(new(IdentityKitClaims.SecurityStamp, stamp));
		}

		return new ClaimsPrincipal(new ClaimsIdentity(claims));
	}

	private static IdentityKitUser User(Guid userId, string stamp = "s1", bool enabled = true)
	{
		return new IdentityKitUser { Id = userId, Email = "a@b.c", SecurityStamp = stamp, Enabled = enabled };
	}

	[Fact]
	public async Task Matching_stamp_keeps_the_principal()
	{
		var userId = Guid.NewGuid();
		_repo.GetAsync(Arg.Any<Guid>()).Returns(User(userId));
		var context = Context(Principal(userId));

		await Validator().ValidateAsync(context);

		Assert.NotNull(context.Principal);
	}

	[Fact]
	public async Task Changed_stamp_rejects_the_principal()
	{
		var userId = Guid.NewGuid();
		_repo.GetAsync(Arg.Any<Guid>()).Returns(User(userId, stamp: "s2"));
		var context = Context(Principal(userId, "s1"));

		await Validator().ValidateAsync(context);

		Assert.Null(context.Principal);
	}

	[Fact]
	public async Task Disabled_user_is_rejected()
	{
		var userId = Guid.NewGuid();
		_repo.GetAsync(Arg.Any<Guid>()).Returns(User(userId, enabled: false));
		var context = Context(Principal(userId));

		await Validator().ValidateAsync(context);

		Assert.Null(context.Principal);
	}

	[Fact]
	public async Task Unknown_user_is_rejected()
	{
		_repo.GetAsync(Arg.Any<Guid>()).Returns((IdentityKitUser?)null);
		var context = Context(Principal(Guid.NewGuid()));

		await Validator().ValidateAsync(context);

		Assert.Null(context.Principal);
	}

	[Fact]
	public async Task Session_without_stamp_claim_is_left_alone()
	{
		var context = Context(Principal(Guid.NewGuid(), stamp: null));

		await Validator().ValidateAsync(context);

		Assert.NotNull(context.Principal);
		_factory.DidNotReceiveWithAnyArgs().Create<IdentityKitUser>();
	}

	[Fact]
	public async Task Cached_stamp_skips_the_database()
	{
		var userId = Guid.NewGuid();
		_repo.GetAsync(Arg.Any<Guid>()).Returns(User(userId));
		var validator = Validator(new IdentityKitOption
		{
			CodeHashPepper = "p",
			SecurityStampValidationInterval = TimeSpan.FromMinutes(30),
		});

		await validator.ValidateAsync(Context(Principal(userId)));
		await validator.ValidateAsync(Context(Principal(userId)));

		await _repo.Received(1).GetAsync(Arg.Any<Guid>());
	}

	[Fact]
	public async Task Cache_expiry_rechecks_and_rejects_a_rotated_stamp()
	{
		var userId = Guid.NewGuid();
		_repo.GetAsync(Arg.Any<Guid>()).Returns(User(userId, stamp: "s1"), User(userId, stamp: "s2"));
		var validator = Validator(new IdentityKitOption
		{
			CodeHashPepper = "p",
			SecurityStampValidationInterval = TimeSpan.FromMilliseconds(1),
		});

		var first = Context(Principal(userId, "s1"));
		await validator.ValidateAsync(first);
		Assert.NotNull(first.Principal);

		// кэш на 1мс истёк — второй заход перечитывает базу
		await Task.Delay(50);

		var second = Context(Principal(userId, "s1"));
		await validator.ValidateAsync(second);
		Assert.Null(second.Principal);
	}
}
