using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RShared.IdentityKit;
using RShared.Orm;
using RShared.Orm.EntityFrameworkCore;

namespace RShared.IdentityKit;

/// <summary>
/// One time codes in the database. Only HMAC-SHA256(pepper, code) hashes are stored —
/// the hash gives an indexed lookup and survives a database leak (the pepper lives in app secrets).
/// Consumption is a conditional update outside any unit of work on purpose: atomic on the
/// database level, and burning a code before the rest of the flow is the safe direction —
/// the user simply asks for a new one.
/// </summary>
public sealed class EfOneTimeCodeStore(
	IEntityRepositoryFactory factory,
	IdentityKitOption option) : IOneTimeCodeStore
{
	/// <inheritdoc />
	public async Task<string> IssueAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string destination, TimeSpan lifetime)
	{
		var code = OneTimeCodeGenerator.Generate();

		await using var uow = factory.CreateUnitOfWork();
		var codes = factory.Create<OneTimeCode>();

		// аннулируем активные коды той же тройки ДО вставки: упавшая вставка оставит юзера
		// без активного кода — безопасное направление, старый код не оживает
		await codes.Query().Where(c => c.Channel == channel && c.Purpose == purpose
			&& c.Destination == destination && c.ConsumedAt == null)
			.ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, DateTime.UtcNow));

		await codes.InsertAsync(new OneTimeCode
		{
			Id = Guid.CreateVersion7(),
			Purpose = purpose,
			Channel = channel,
			Destination = destination,
			CodeHash = Hash(code),
			ExpiresAt = DateTime.UtcNow.Add(lifetime),
		});
		await uow.CommitAsync();

		return code;
	}

	/// <inheritdoc />
	public async Task<string?> TakeAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string code)
	{
		var candidate = await FindActiveAsync(channel, purpose, destination: null, code);
		if (candidate is null)
		{
			return null;
		}

		return await ConsumeAsync(candidate.Id) ? candidate.Destination : null;
	}

	/// <inheritdoc />
	public async Task<bool> TakeAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string destination, string code)
	{
		var candidate = await FindActiveAsync(channel, purpose, destination, code);
		return candidate is not null && await ConsumeAsync(candidate.Id);
	}

	private async Task<OneTimeCode?> FindActiveAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string? destination, string code)
	{
		// Stryker disable once Equality : граница > vs >= — недоказуемо стабильным тестом
		var query = factory.Create<OneTimeCode>().Query()
			.Where(c => c.CodeHash == Hash(code) && c.Channel == channel && c.Purpose == purpose
				&& c.ConsumedAt == null && c.ExpiresAt > DateTime.UtcNow);

		if (destination is not null)
		{
			query = query.Where(c => c.Destination == destination);
		}

		return await query.FirstOrDefaultAsync();
	}

	private async Task<bool> ConsumeAsync(Guid id)
	{
		// условное потребление: гонка двух Take оставляет одному affected == 0
		var affected = await factory.Create<OneTimeCode>().Query()
			.Where(c => c.Id == id && c.ConsumedAt == null)
			.ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, DateTime.UtcNow));
		return affected == 1;
	}

	private string Hash(string code)
	{
		var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(option.CodeHashPepper), Encoding.UTF8.GetBytes(code));
		return Convert.ToHexString(mac);
	}
}
