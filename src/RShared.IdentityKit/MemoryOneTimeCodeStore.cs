namespace RShared.IdentityKit;

/// <summary>
/// Single use codes with a TTL, kept in process memory.
/// Enough for a single node; register another <see cref="IOneTimeCodeStore"/> implementation
/// (Redis, a database table) for multi node farms.
/// </summary>
public sealed class MemoryOneTimeCodeStore : IOneTimeCodeStore
{
	private readonly object _gate = new();
	private readonly List<CodeEntry> _codes = [];

	private sealed record CodeEntry(string Code, OneTimeCodeChannel Channel, OneTimeCodePurpose Purpose, string Destination, DateTimeOffset ExpiresAt);

	public Task<string> IssueAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string destination, TimeSpan lifetime)
	{
		var code = OneTimeCodeGenerator.Generate();

		lock (_gate)
		{
			// Stryker disable Statement : чистка протухших кодов не видна снаружи, это защита от роста списка
			DropExpired(DateTimeOffset.UtcNow);
			// новый код той же тройки аннулирует предыдущий: активный код на назначение — один
			_codes.RemoveAll(e => e.Channel == channel && e.Purpose == purpose && e.Destination == destination);
			_codes.Add(new CodeEntry(code, channel, purpose, destination, DateTimeOffset.UtcNow.Add(lifetime)));
		}

		return Task.FromResult(code);
	}

	public Task<string?> TakeAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string code)
	{
		lock (_gate)
		{
			DropExpired(DateTimeOffset.UtcNow);
			var entry = _codes.FirstOrDefault(e => e.Channel == channel && e.Purpose == purpose && e.Code == code);
			if (entry is null)
			{
				return Task.FromResult((string?)null);
			}

			_codes.Remove(entry);
			return Task.FromResult((string?)entry.Destination);
		}
	}

	public Task<bool> TakeAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string destination, string code)
	{
		lock (_gate)
		{
			DropExpired(DateTimeOffset.UtcNow);
			var removed = _codes.RemoveAll(e => e.Channel == channel && e.Purpose == purpose
				&& e.Destination == destination && e.Code == code) > 0;
			return Task.FromResult(removed);
		}
	}

	private void DropExpired(DateTimeOffset now)
	{
		// Stryker disable Equality : граница <= vs < — дегенератный нулевой TTL, недоказуемо стабильным тестом
		_codes.RemoveAll(e => e.ExpiresAt <= now);
	}
}
