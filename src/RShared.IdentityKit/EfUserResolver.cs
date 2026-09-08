using Microsoft.EntityFrameworkCore;
using RShared.AuthKit;
using RShared.Orm;
using RShared.Orm.EntityFrameworkCore;

namespace RShared.IdentityKit;

/// <summary>
/// Resolves external identities over external_accounts and users.
/// Order: a known link; the password login (email); optional link-by-email (A6, verified-email
/// providers only); optional user creation on the first external sign in.
/// </summary>
public class EfUserResolver<TUser>(
	IEntityRepositoryFactory factory,
	IdentityKitOption option) : IAuthKitUserResolver
	where TUser : IdentityKitUser, new()
{
	// провайдеры, чей email-клейм считается верифицированным: только они участвуют в link-by-email
	// и только им создаётся EmailConfirmed при первом входе
	private static readonly HashSet<string> VerifiedEmailProviders = ["google"];

	/// <inheritdoc />
	public async Task<AuthKitUser?> ResolveAsync(ExternalIdentity identity)
	{
		var accounts = factory.Create<ExternalAccount>();
		var account = await accounts.Query()
			.SingleOrDefaultAsync(a => a.Provider == identity.Provider && a.ExternalId == identity.Id);

		if (account is not null)
		{
			var linked = await FindUserAsync(account.UserId);
			return linked is { Enabled: true } ? ToAuthKitUser(linked) : null;
		}

		if (identity.Provider == "password")
		{
			// после успешной валидации пароля логин выступает внешним id — это email
			var byLogin = await factory.Create<TUser>().Query()
				.SingleOrDefaultAsync(u => u.Email == EmailNormalizer.Normalize(identity.Id));
			return byLogin is { Enabled: true } ? ToAuthKitUser(byLogin) : null;
		}

		if (option.LinkByEmail && VerifiedEmailProviders.Contains(identity.Provider)
			&& !string.IsNullOrWhiteSpace(identity.Email))
		{
			var linkedByEmail = await TryLinkByEmailAsync(identity, accounts);
			if (linkedByEmail is not null)
			{
				return linkedByEmail;
			}
		}

		if (!option.CreateUsersOnFirstExternalSignIn)
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(identity.Email))
		{
			// без email юзера не создаём: нечем адресовать коды и сброс пароля
			return null;
		}

		var canonical = EmailNormalizer.Normalize(identity.Email);
		if (await factory.Create<TUser>().Query().AnyAsync(u => u.Email == canonical))
		{
			// email занят, а линковка выключена: создать дубль нельзя, молча не пускаем
			return null;
		}

		var created = await CreateUserAsync(identity);
		await using var uow = factory.CreateUnitOfWork();
		await factory.Create<TUser>().InsertAsync(created);
		await accounts.InsertAsync(new ExternalAccount
		{
			Id = Guid.CreateVersion7(),
			Provider = identity.Provider,
			ExternalId = identity.Id,
			UserId = created.Id,
			CreatedAt = DateTime.UtcNow,
		});
		await uow.CommitAsync();
		return ToAuthKitUser(created);
	}

	/// <summary>
	/// Hook: builds the user for a first external sign in. Override to fill profile fields.
	/// </summary>
	protected internal virtual Task<TUser> CreateUserAsync(ExternalIdentity identity)
	{
		return Task.FromResult(new TUser
		{
			Id = Guid.CreateVersion7(),
			Email = EmailNormalizer.Normalize(identity.Email!),
			EmailConfirmed = VerifiedEmailProviders.Contains(identity.Provider),
			Roles = [],
			SecurityStamp = Stamp.New(),
			CreatedAt = DateTime.UtcNow,
		});
	}

	private async Task<AuthKitUser?> TryLinkByEmailAsync(ExternalIdentity identity, IEntityRepository<ExternalAccount> accounts)
	{
		var email = EmailNormalizer.Normalize(identity.Email!);
		var user = await factory.Create<TUser>().Query()
			.SingleOrDefaultAsync(u => u.Email == email);
		if (user is null)
		{
			return null;
		}

		await using var uow = factory.CreateUnitOfWork();
		await accounts.InsertAsync(new ExternalAccount
		{
			Id = Guid.CreateVersion7(),
			Provider = identity.Provider,
			ExternalId = identity.Id,
			UserId = user.Id,
			CreatedAt = DateTime.UtcNow,
		});
		// верификация email провайдером — доказательство владения адресом;
		// вставка аккаунта регистрирует контекст в UoW, коммит флашит и правку юзера одним SaveChanges
		user.EmailConfirmed = true;
		await uow.CommitAsync();

		return user.Enabled ? ToAuthKitUser(user) : null;
	}

	private async Task<TUser?> FindUserAsync(Guid userId)
	{
		return await factory.Create<TUser>().GetAsync(userId);
	}

	internal static AuthKitUser ToAuthKitUser(TUser user)
	{
		var claims = new List<System.Security.Claims.Claim>(user.Roles.Count + 1)
		{
			new(IdentityKitClaims.SecurityStamp, user.SecurityStamp),
		};
		claims.AddRange(user.Roles.Select(role => new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)));

		return new AuthKitUser(user.Id.ToString(), Claims: claims);
	}
}
