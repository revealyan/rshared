using Microsoft.EntityFrameworkCore;
using RShared.AuthKit;
using RShared.Orm.EntityFrameworkCore;
using RShared.IdentityKit;
using RShared.Orm;

using Xunit;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Смена пароля и блокировка: проверка текущего пароля, ротация stamp, админ-действие
/// </summary>
public sealed class ChangePasswordTests
{
	[Fact]
	public async Task Wrong_current_password_returns_false()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");

		Assert.False(await kit.ChangePasswordAsync(registered.UserId, "wrong", "newpass"));
		Assert.True(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "secret"));
	}

	[Fact]
	public async Task Success_rotates_password_and_stamp()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");
		var oldStamp = await StampOfAsync(scope, registered.UserId);

		Assert.True(await kit.ChangePasswordAsync(registered.UserId, "secret", "newpass"));

		Assert.False(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "secret"));
		Assert.True(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "newpass"));
		Assert.NotEqual(oldStamp, await StampOfAsync(scope, registered.UserId));
	}

	[Fact]
	public async Task Unknown_user_returns_false()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		Assert.False(await scope.Get<IIdentityKit>().ChangePasswordAsync(Guid.NewGuid(), "a", "b"));
	}

	[Fact]
	public async Task SetEnabled_unknown_user_throws()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => scope.Get<IIdentityKit>().SetEnabledAsync(Guid.NewGuid(), true));

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => scope.Get<IIdentityKit>().SetEnabledAsync(Guid.NewGuid(), false));
		Assert.Contains("is not found", ex.Message);
	}

	[Fact]
	public async Task SetEnabled_disables_and_reenables()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");

		await kit.SetEnabledAsync(registered.UserId, false);
		Assert.False(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "secret"));

		await kit.SetEnabledAsync(registered.UserId, true);
		Assert.True(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "secret"));
	}

	private static async Task<string> StampOfAsync(IdentityKitFixture.ScopeHandle scope, Guid userId)
	{
		// AsNoTracking: проверяем записанное в БД, а не прижизненные правки трекнутой сущности
		var user = await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query().AsNoTracking()
			.SingleAsync(u => u.Id == userId);
		return user.SecurityStamp;
	}
}
