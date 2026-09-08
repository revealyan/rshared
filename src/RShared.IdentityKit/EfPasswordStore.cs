using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RShared.AuthKit;
using RShared.Orm;
using RShared.Orm.EntityFrameworkCore;

namespace RShared.IdentityKit;

/// <summary>
/// Password validation over the users table: canonical login lookup + PasswordHasher verify.
/// Deliberately read-only: a rehash on <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>
/// would require a unit of work inside a validation seam.
/// </summary>
public sealed class EfPasswordStore<TUser>(
	IEntityRepositoryFactory factory,
	IPasswordHasher<TUser> hasher) : IAuthKitPasswordStore
	where TUser : IdentityKitUser
{
	/// <inheritdoc />
	public async Task<bool> ValidateAsync(string login, string password)
	{
		if (string.IsNullOrWhiteSpace(login))
		{
			return false;
		}

		var email = EmailNormalizer.Normalize(login);
		var user = await factory.Create<TUser>().Query().AsNoTracking()
			.SingleOrDefaultAsync(u => u.Email == email);

		return user is { Enabled: true, PasswordHash: not null }
			&& hasher.VerifyHashedPassword(user, user.PasswordHash, password)
				is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
	}
}
