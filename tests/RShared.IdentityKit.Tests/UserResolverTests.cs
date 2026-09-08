using Xunit;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RShared.IdentityKit;
using RShared.Orm;
using RShared.Orm.EntityFrameworkCore;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Резолвер внешних идентичностей: связки, парольная ветка, link-by-email (A6),
/// создание юзера при первом входе и его политики
/// </summary>
public sealed class UserResolverTests
{
	[Fact]
	public async Task Resolves_a_known_external_account()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var userId = await InsertUserWithGoogleAccountAsync(scope, enabled: true);

		var resolved = await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1"));

		Assert.NotNull(resolved);
		Assert.Equal(userId.ToString(), resolved.Id);
	}

	[Fact]
	public async Task Disabled_linked_user_resolves_to_null()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		await InsertUserWithGoogleAccountAsync(scope, enabled: false);

		Assert.Null(await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1")));
	}

	[Fact]
	public async Task Password_identity_resolves_by_canonical_email()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var registered = await scope.Get<IIdentityKit>().RegisterAsync("A@B.C", "secret");

		var resolved = await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("password", "a@b.c"));

		Assert.NotNull(resolved);
		Assert.Equal(registered.UserId.ToString(), resolved.Id);
	}

	[Fact]
	public async Task First_google_sign_in_creates_a_user_with_confirmed_email()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		var resolved = await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1", "guy@x.y"));

		Assert.NotNull(resolved);
		var user = await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query().AsNoTracking()
			.SingleAsync(u => u.Email == "guy@x.y");
		Assert.True(user.EmailConfirmed);
		Assert.Equal(user.Id.ToString(), resolved!.Id);
	}

	[Fact]
	public async Task Creation_off_returns_null_for_unknown_identity()
	{
		using var fixture = IdentityKitFixture.Create(o => o.CreateUsersOnFirstExternalSignIn = false);
		using var scope = fixture.OpenScope();

		Assert.Null(await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1", "guy@x.y")));
	}

	[Fact]
	public async Task Identity_without_email_is_not_created()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		Assert.Null(await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1")));
	}

	[Fact]
	public async Task Taken_email_without_linking_returns_null_and_creates_nothing()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		await scope.Get<IIdentityKit>().RegisterAsync("guy@x.y", "secret");

		Assert.Null(await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1", "GUY@X.Y")));
		Assert.Equal(1, await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query().CountAsync());
	}

	[Fact]
	public async Task LinkByEmail_is_off_by_default()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		await scope.Get<IIdentityKit>().RegisterAsync("guy@x.y", "secret");

		var resolved = await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1", "guy@x.y"));

		// линковки нет, а создать дубль email нельзя: вход не состоялся
		Assert.Null(resolved);
	}

	[Fact]
	public async Task LinkByEmail_links_and_confirms_when_enabled()
	{
		using var fixture = IdentityKitFixture.Create(o => o.LinkByEmail = true);
		using var scope = fixture.OpenScope();
		var registered = await scope.Get<IIdentityKit>().RegisterAsync("guy@x.y", "secret");

		var resolved = await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1", "guy@x.y"));

		Assert.NotNull(resolved);
		Assert.Equal(registered.UserId.ToString(), resolved.Id);
		var user = await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query().AsNoTracking()
			.SingleAsync(u => u.Email == "guy@x.y");
		Assert.True(user.EmailConfirmed);
		Assert.True(await scope.Get<IEntityRepositoryFactory>().Create<ExternalAccount>().Query()
			.AnyAsync(a => a.Provider == "google" && a.ExternalId == "g1" && a.UserId == user.Id));
	}

	[Fact]
	public async Task LinkByEmail_ignores_providers_without_verified_email()
	{
		using var fixture = IdentityKitFixture.Create(o => o.LinkByEmail = true);
		using var scope = fixture.OpenScope();
		await scope.Get<IIdentityKit>().RegisterAsync("guy@x.y", "secret");

		// провайдер вне VerifiedEmailProviders: линковки нет, а создать дубль email нельзя
		Assert.Null(await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("facebook", "f1", "guy@x.y")));
	}

	[Fact]
	public async Task Resolved_claims_carry_roles_and_stamp()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		await scope.Get<IIdentityKit>().RegisterAsync("a@b.c", "secret", ["admin"]);

		var resolved = await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("password", "a@b.c"));

		Assert.NotNull(resolved?.Claims);
		Assert.Contains(resolved.Claims, c => c.Type == ClaimTypes.Role && c.Value == "admin");
		Assert.Contains(resolved.Claims, c => c.Type == IdentityKitClaims.SecurityStamp && !string.IsNullOrEmpty(c.Value));
	}

	[Fact]
	public async Task Password_identity_returns_null_for_disabled_user()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");
		await kit.SetEnabledAsync(registered.UserId, false);

		Assert.Null(await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("password", "a@b.c")));
	}

	[Fact]
	public async Task LinkByEmail_returns_null_for_disabled_user()
	{
		using var fixture = IdentityKitFixture.Create(o => o.LinkByEmail = true);
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("guy@x.y", "secret");
		await kit.SetEnabledAsync(registered.UserId, false);

		Assert.Null(await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1", "guy@x.y")));
	}

	[Fact]
	public async Task Accounts_of_different_providers_with_same_external_id_do_not_mix()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var googleUser = await InsertUserWithAccountAsync(scope, "google", "g1", "a@b.c");
		var telegramUser = await InsertUserWithAccountAsync(scope, "telegram", "g1", "x@y.z");

		var byGoogle = await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g1"));
		var byTelegram = await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("telegram", "g1"));

		Assert.Equal(googleUser.ToString(), byGoogle!.Id);
		Assert.Equal(telegramUser.ToString(), byTelegram!.Id);
	}

	[Fact]
	public async Task CreateUserAsync_hook_shapes_the_created_user()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var calls = new List<string>();
		var resolver = new HookedResolver(scope.Get<IEntityRepositoryFactory>(), scope.Get<IdentityKitOption>(), calls);

		var resolved = await resolver.ResolveAsync(new ExternalIdentity("google", "g9", "guy@x.y"));

		Assert.Equal(["g9"], calls);
		var user = await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query().AsNoTracking()
			.SingleAsync(u => u.Email == "guy@x.y");
		Assert.Equal(["custom"], user.Roles);
		Assert.Equal(user.Id.ToString(), resolved?.Id);
	}

	private static async Task<Guid> InsertUserWithGoogleAccountAsync(IdentityKitFixture.ScopeHandle scope, bool enabled)
	{
		return await InsertUserWithAccountAsync(scope, "google", "g1", "a@b.c", enabled);
	}

	private static async Task<Guid> InsertUserWithAccountAsync(
		IdentityKitFixture.ScopeHandle scope, string provider, string externalId, string email, bool enabled = true)
	{
		var factory = scope.Get<IEntityRepositoryFactory>();
		var userId = Guid.CreateVersion7();

		await using (var uow = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<IdentityKitUser>().InsertAsync(new IdentityKitUser
			{
				Id = userId,
				Email = email,
				SecurityStamp = Stamp.New(),
				Enabled = enabled,
				CreatedAt = DateTime.UtcNow,
			});
			await factory.Create<ExternalAccount>().InsertAsync(new ExternalAccount
			{
				Id = Guid.CreateVersion7(),
				Provider = provider,
				ExternalId = externalId,
				UserId = userId,
				CreatedAt = DateTime.UtcNow,
			});
			await uow.CommitAsync();
		}

		return userId;
	}

	private sealed class HookedResolver(
		IEntityRepositoryFactory factory,
		IdentityKitOption option,
		List<string> calls) : EfUserResolver<IdentityKitUser>(factory, option)
	{
		protected internal override Task<IdentityKitUser> CreateUserAsync(ExternalIdentity identity)
		{
			calls.Add(identity.Id);
			return Task.FromResult(new IdentityKitUser
			{
				Id = Guid.CreateVersion7(),
				Email = EmailNormalizer.Normalize(identity.Email!),
				EmailConfirmed = true,
				Roles = ["custom"],
				SecurityStamp = Stamp.New(),
				CreatedAt = DateTime.UtcNow,
			});
		}
	}

	[Fact]
	public async Task LinkByEmail_without_existing_user_falls_back_to_creation()
	{
		using var fixture = IdentityKitFixture.Create(o => o.LinkByEmail = true);
		using var scope = fixture.OpenScope();

		var resolved = await scope.Get<IAuthKitUserResolver>().ResolveAsync(new ExternalIdentity("google", "g9", "stranger@x.y"));

		Assert.NotNull(resolved);
		var user = await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query().AsNoTracking()
			.SingleAsync(u => u.Email == "stranger@x.y");
		Assert.Equal(user.Id.ToString(), resolved!.Id);
	}
}
