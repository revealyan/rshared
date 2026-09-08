using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RShared.AuthKit;
using RShared.Orm;
using RShared.Orm.EntityFrameworkCore;

namespace RShared.IdentityKit;

/// <summary>
/// IIdentityKit implementation over AuthKit, Orm and PasswordHasher.
/// Email flows are silent about unknown, disabled and unconfirmed accounts —
/// they must not reveal account existence.
/// </summary>
internal sealed class IdentityKitService<TUser>(
	IAuthKit authKit,
	IEntityRepositoryFactory factory,
	IPasswordHasher<TUser> hasher,
	IEmailSender? emailSender,
	IOneTimeCodeStore codes,
	IdentityKitOption option) : IIdentityKit
	where TUser : IdentityKitUser, new()
{
	public async Task<RegisterResult> RegisterAsync(string email, string password, IEnumerable<string>? roles = null)
	{
		var canonical = EmailNormalizer.Normalize(email);
		var users = factory.Create<TUser>();

		if (await users.Query().AnyAsync(u => u.Email == canonical))
		{
			return new RegisterResult(RegisterStatus.DuplicateEmail, Guid.Empty);
		}

		var user = new TUser
		{
			Id = Guid.CreateVersion7(),
			Email = canonical,
			Roles = roles?.ToList() ?? [],
			SecurityStamp = Stamp.New(),
			CreatedAt = DateTime.UtcNow,
		};
		user.PasswordHash = hasher.HashPassword(user, password);

		await using (var uow = factory.CreateUnitOfWork())
		{
			await users.InsertAsync(user);
			var code = await codes.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.EmailConfirm, canonical, option.CodeLifetime);
			await Sender().SendCodeAsync(canonical, OneTimeCodePurpose.EmailConfirm, code);
			await uow.CommitAsync();
		}

		return new RegisterResult(RegisterStatus.Success, user.Id);
	}

	public async Task SendEmailCodeAsync(string email)
	{
		var user = await FindEnabledConfirmedAsync(email);
		if (user is null)
		{
			return;
		}

		var code = await codes.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, user.Email, option.CodeLifetime);
		await Sender().SendCodeAsync(user.Email, OneTimeCodePurpose.Login, code);
	}

	public async Task<bool> EmailCodeSignInAsync(string email, string code)
	{
		var canonical = EmailNormalizer.Normalize(email);
		if (!await codes.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, canonical, code.Trim()))
		{
			return false;
		}

		var user = await factory.Create<TUser>().Query().SingleOrDefaultAsync(u => u.Email == canonical);
		if (user is not { Enabled: true })
		{
			return false;
		}

		await authKit.SignInAsync(EfUserResolver<TUser>.ToAuthKitUser(user), option.SessionLifetime);
		return true;
	}

	public async Task RequestPasswordResetAsync(string email)
	{
		// подтверждённость не требуем: сброс сам доказывает владение адресом и подтверждает его
		var user = await FindEnabledAsync(email);
		if (user is null)
		{
			return;
		}

		var code = await codes.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.PasswordReset, user.Email, option.CodeLifetime);
		await Sender().SendCodeAsync(user.Email, OneTimeCodePurpose.PasswordReset, code);
	}

	public async Task<bool> ResetPasswordAsync(string email, string code, string newPassword)
	{
		var canonical = EmailNormalizer.Normalize(email);
		if (!await codes.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.PasswordReset, canonical, code.Trim()))
		{
			return false;
		}

		var user = await factory.Create<TUser>().Query().SingleOrDefaultAsync(u => u.Email == canonical);
		if (user is null)
		{
			return false;
		}

		await using var uow = factory.CreateUnitOfWork();
		user.PasswordHash = hasher.HashPassword(user, newPassword);
		user.SecurityStamp = Stamp.New();
		// получение кода на почту — доказательство владения адресом
		user.EmailConfirmed = true;
		await factory.Create<TUser>().AddAsync(user);
		await uow.CommitAsync();
		return true;
	}

	public async Task ResendEmailConfirmationAsync(string email)
	{
		// подтверждённости не требуем — её и добиваемся повторной отправкой
		var user = await FindEnabledAsync(email);
		if (user is null || user.EmailConfirmed)
		{
			return;
		}

		var code = await codes.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.EmailConfirm, user.Email, option.CodeLifetime);
		await Sender().SendCodeAsync(user.Email, OneTimeCodePurpose.EmailConfirm, code);
	}

	public async Task<bool> ConfirmEmailAsync(string email, string code)
	{
		var canonical = EmailNormalizer.Normalize(email);
		if (!await codes.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.EmailConfirm, canonical, code.Trim()))
		{
			return false;
		}

		var user = await factory.Create<TUser>().Query().SingleOrDefaultAsync(u => u.Email == canonical);
		if (user is null)
		{
			return false;
		}

		await using var uow = factory.CreateUnitOfWork();
		user.EmailConfirmed = true;
		await factory.Create<TUser>().AddAsync(user);
		await uow.CommitAsync();
		return true;
	}

	public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
	{
		var user = await factory.Create<TUser>().GetAsync(userId);
		if (user is not { Enabled: true, PasswordHash: not null })
		{
			return false;
		}

		if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword)
			is not (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
		{
			return false;
		}

		await using var uow = factory.CreateUnitOfWork();
		user.PasswordHash = hasher.HashPassword(user, newPassword);
		user.SecurityStamp = Stamp.New();
		await factory.Create<TUser>().AddAsync(user);
		await uow.CommitAsync();
		return true;
	}

	public async Task SetEnabledAsync(Guid userId, bool enabled)
	{
		var user = await factory.Create<TUser>().GetAsync(userId)
			?? throw new InvalidOperationException($"IdentityKit: user \"{userId}\" is not found");

		await using var uow = factory.CreateUnitOfWork();
		user.Enabled = enabled;
		await factory.Create<TUser>().AddAsync(user);
		await uow.CommitAsync();
	}

	private async Task<TUser?> FindEnabledAsync(string email)
	{
		var canonical = EmailNormalizer.Normalize(email);
		return await factory.Create<TUser>().Query()
			.SingleOrDefaultAsync(u => u.Email == canonical && u.Enabled);
	}

	private async Task<TUser?> FindEnabledConfirmedAsync(string email)
	{
		var canonical = EmailNormalizer.Normalize(email);
		return await factory.Create<TUser>().Query()
			.SingleOrDefaultAsync(u => u.Email == canonical && u.Enabled && u.EmailConfirmed);
	}

	private IEmailSender Sender()
	{
		return emailSender ?? throw new InvalidOperationException("IdentityKit: IEmailSender is not registered");
	}
}
