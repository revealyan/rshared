using Xunit;
using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RShared.IdentityKit;
using NSubstitute;
using RShared.Orm;
using RShared.Orm.EntityFrameworkCore;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Email-потоки: регистрация с кодом подтверждения, вход по коду (включая анти-enumeration),
/// сброс пароля со регенерацией stamp, подтверждение email, отсутствие IEmailSender
/// </summary>
public sealed class EmailFlowTests
{
	private static FakeEmailSender Sender(IdentityKitFixture.ScopeHandle scope)
	{
		return (FakeEmailSender)scope.Get<IEmailSender>();
	}

	[Fact]
	public async Task Register_creates_user_and_sends_confirmation_code()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		var result = await scope.Get<IIdentityKit>().RegisterAsync("a@b.c", "secret");

		Assert.Equal(RegisterStatus.Success, result.Status);
		var sent = Assert.Single(Sender(scope).Sent);
		Assert.Equal(("a@b.c", OneTimeCodePurpose.EmailConfirm, sent.Code), sent);
	}

	[Fact]
	public async Task SendEmailCode_is_silent_for_unknown_email()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		await scope.Get<IIdentityKit>().SendEmailCodeAsync("nobody@x.y");

		Assert.Empty(Sender(scope).Sent);
	}

	[Fact]
	public async Task SendEmailCode_is_silent_for_unconfirmed_email()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		await scope.Get<IIdentityKit>().RegisterAsync("a@b.c", "secret");

		await scope.Get<IIdentityKit>().SendEmailCodeAsync("a@b.c");

		// отправлен только код подтверждения из регистрации, логин-кода нет
		var sent = Assert.Single(Sender(scope).Sent);
		Assert.Equal(OneTimeCodePurpose.EmailConfirm, sent.Purpose);
	}

	[Fact]
	public async Task SendEmailCode_is_silent_for_disabled_user()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");
		await ConfirmAsync(kit, scope, "a@b.c");
		await kit.SetEnabledAsync(registered.UserId, false);

		await kit.SendEmailCodeAsync("a@b.c");

		Assert.DoesNotContain(Sender(scope).Sent, s => s.Purpose == OneTimeCodePurpose.Login);
	}

	[Fact]
	public async Task Email_code_signs_in_and_is_single_use()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await RegisterConfirmedAsync(kit, scope, "a@b.c", "secret");

		await kit.SendEmailCodeAsync("a@b.c");
		var code = LastCode(scope, OneTimeCodePurpose.Login);

		Assert.True(await kit.EmailCodeSignInAsync("a@b.c", $"  {code}  "));
		await fixture.Auth.Received(1).SignInAsync(Arg.Any<HttpContext>(), "RShared.AuthKit",
			Arg.Is<ClaimsPrincipal>(p => p.FindFirst(ClaimTypes.NameIdentifier) != null),
			Arg.Any<AuthenticationProperties>());

		Assert.False(await kit.EmailCodeSignInAsync("a@b.c", code));
	}

	[Fact]
	public async Task Email_code_sign_in_rejects_a_reset_code()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await RegisterConfirmedAsync(kit, scope, "a@b.c", "secret");

		await kit.RequestPasswordResetAsync("a@b.c");
		var code = LastCode(scope, OneTimeCodePurpose.PasswordReset);

		Assert.False(await kit.EmailCodeSignInAsync("a@b.c", code));
	}

	[Fact]
	public async Task Email_code_sign_in_rejects_unknown_email()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		Assert.False(await scope.Get<IIdentityKit>().EmailCodeSignInAsync("nobody@x.y", "ZZZZZZZZ"));
	}

	[Fact]
	public async Task RequestPasswordReset_is_silent_for_unknown_email()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();

		await scope.Get<IIdentityKit>().RequestPasswordResetAsync("nobody@x.y");

		Assert.Empty(Sender(scope).Sent);
	}

	[Fact]
	public async Task RequestPasswordReset_is_silent_for_disabled_user()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");
		await kit.SetEnabledAsync(registered.UserId, false);
		Sender(scope).Sent.Clear();

		await kit.RequestPasswordResetAsync("a@b.c");

		Assert.DoesNotContain(Sender(scope).Sent, s => s.Purpose == OneTimeCodePurpose.PasswordReset);
	}

	[Fact]
	public async Task ResendEmailConfirmation_sends_a_working_code_for_unconfirmed()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");
		Sender(scope).Sent.Clear();

		await kit.ResendEmailConfirmationAsync("a@b.c");

		var sent = Assert.Single(Sender(scope).Sent);
		Assert.Equal(OneTimeCodePurpose.EmailConfirm, sent.Purpose);
		Assert.True(await kit.ConfirmEmailAsync("a@b.c", sent.Code));

		var user = await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query()
			.SingleAsync(u => u.Id == registered.UserId);
		Assert.True(user.EmailConfirmed);
	}

	[Fact]
	public async Task ResetPassword_rotates_password_and_stamp()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");
		var oldStamp = await StampOfAsync(scope, registered.UserId);

		await kit.RequestPasswordResetAsync("a@b.c");
		var code = LastCode(scope, OneTimeCodePurpose.PasswordReset);

		Assert.True(await kit.ResetPasswordAsync("a@b.c", code, "newpass"));
		Assert.False(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "secret"));
		Assert.True(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "newpass"));
		Assert.NotEqual(oldStamp, await StampOfAsync(scope, registered.UserId));
	}

	[Fact]
	public async Task ResetPassword_confirms_the_email()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");

		await kit.RequestPasswordResetAsync("a@b.c");
		var code = LastCode(scope, OneTimeCodePurpose.PasswordReset);
		await kit.ResetPasswordAsync("a@b.c", code, "newpass");

		var user = await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query().AsNoTracking()
			.SingleAsync(u => u.Id == registered.UserId);
		Assert.True(user.EmailConfirmed);
	}

	[Fact]
	public async Task ResetPassword_rejects_wrong_code()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await kit.RegisterAsync("a@b.c", "secret");

		Assert.False(await kit.ResetPasswordAsync("a@b.c", "ZZZZZZZZ", "newpass"));
		Assert.True(await scope.Get<IAuthKit>().PasswordSignInAsync("a@b.c", "secret"));
	}

	[Fact]
	public async Task ConfirmEmail_sets_the_flag()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		var registered = await kit.RegisterAsync("a@b.c", "secret");

		Assert.True(await ConfirmAsync(kit, scope, "a@b.c"));

		var user = await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query().AsNoTracking()
			.SingleAsync(u => u.Id == registered.UserId);
		Assert.True(user.EmailConfirmed);
	}

	[Fact]
	public async Task ResendEmailConfirmation_is_silent_for_confirmed_email()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await RegisterConfirmedAsync(kit, scope, "a@b.c", "secret");
		Sender(scope).Sent.Clear();

		await kit.ResendEmailConfirmationAsync("a@b.c");

		Assert.Empty(Sender(scope).Sent);
	}

	[Fact]
	public async Task Missing_email_sender_throws()
	{
		using var fixture = IdentityKitFixture.Create(withEmailSender: false);
		using var scope = fixture.OpenScope();
		var factory = scope.Get<IEntityRepositoryFactory>();
		await using (var uow = factory.CreateUnitOfWork(IsolationLevel.Serializable))
		{
			await factory.Create<IdentityKitUser>().InsertAsync(new IdentityKitUser
			{
				Id = Guid.CreateVersion7(),
				Email = "a@b.c",
				EmailConfirmed = true,
				SecurityStamp = Stamp.New(),
				CreatedAt = DateTime.UtcNow,
			});
			await uow.CommitAsync();
		}

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => scope.Get<IIdentityKit>().SendEmailCodeAsync("a@b.c"));
		Assert.Contains("IEmailSender", ex.Message);
	}

	private static async Task RegisterConfirmedAsync(IIdentityKit kit, IdentityKitFixture.ScopeHandle scope, string email, string password)
	{
		await kit.RegisterAsync(email, password);
		await ConfirmAsync(kit, scope, email);
	}

	private static async Task<bool> ConfirmAsync(IIdentityKit kit, IdentityKitFixture.ScopeHandle scope, string email)
	{
		var code = LastCode(scope, OneTimeCodePurpose.EmailConfirm);
		return await kit.ConfirmEmailAsync(email, code);
	}

	private static string LastCode(IdentityKitFixture.ScopeHandle scope, OneTimeCodePurpose purpose)
	{
		return Sender(scope).Sent.Last(s => s.Purpose == purpose).Code;
	}

	private static async Task<string> StampOfAsync(IdentityKitFixture.ScopeHandle scope, Guid userId)
	{
		// AsNoTracking: проверяем записанное в БД, а не прижизненные правки трекнутой сущности
		var user = await scope.Get<IEntityRepositoryFactory>().Create<IdentityKitUser>().Query().AsNoTracking()
			.SingleAsync(u => u.Id == userId);
		return user.SecurityStamp;
	}

	[Fact]
	public async Task Email_code_sign_in_rejects_when_user_disappeared_after_send()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await RegisterConfirmedAsync(kit, scope, "a@b.c", "secret");
		await kit.SendEmailCodeAsync("a@b.c");
		var code = LastCode(scope, OneTimeCodePurpose.Login);
		await DeleteUserAsync(scope, "a@b.c");

		Assert.False(await kit.EmailCodeSignInAsync("a@b.c", code));
	}

	[Fact]
	public async Task ResetPassword_rejects_when_user_disappeared_after_request()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await kit.RegisterAsync("a@b.c", "secret");
		await kit.RequestPasswordResetAsync("a@b.c");
		var code = LastCode(scope, OneTimeCodePurpose.PasswordReset);
		await DeleteUserAsync(scope, "a@b.c");

		Assert.False(await kit.ResetPasswordAsync("a@b.c", code, "newpass"));
	}

	[Fact]
	public async Task ConfirmEmail_rejects_wrong_code()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await kit.RegisterAsync("a@b.c", "secret");

		Assert.False(await kit.ConfirmEmailAsync("a@b.c", "ZZZZZZZZ"));
	}

	[Fact]
	public async Task ConfirmEmail_rejects_when_user_disappeared_after_register()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var kit = scope.Get<IIdentityKit>();
		await kit.RegisterAsync("a@b.c", "secret");
		var code = LastCode(scope, OneTimeCodePurpose.EmailConfirm);
		await DeleteUserAsync(scope, "a@b.c");

		Assert.False(await kit.ConfirmEmailAsync("a@b.c", code));
	}

	private static async Task DeleteUserAsync(IdentityKitFixture.ScopeHandle scope, string email)
	{
		var factory = scope.Get<IEntityRepositoryFactory>();
		var user = await factory.Create<IdentityKitUser>().Query().SingleAsync(u => u.Email == email);
		await using var uow = factory.CreateUnitOfWork(System.Data.IsolationLevel.Serializable);
		await factory.Create<IdentityKitUser>().DeleteAsync(user);
		await uow.CommitAsync();
	}
}
