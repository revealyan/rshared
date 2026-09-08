using System.Security.Cryptography;

namespace RShared.AuthKit;

/// <summary>
/// Single use Telegram codes with a TTL, kept in process memory.
/// Enough for a single node; register another <see cref="ITgCodeStore"/> implementation
/// (Redis, a database table) for multi node farms.
/// </summary>
public sealed class MemoryTgCodeStore : ITgCodeStore
{
	// base32 без похожих символов (нет 0/1/I/L/O/U): 8 знаков ≈ 10^12 комбинаций,
	// перебор снаружи безо всякого rate limit становится бессмысленным
	private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

	private readonly object _gate = new();
	private readonly Dictionary<string, (string UserId, DateTimeOffset ExpiresAt)> _codes = [];

	public Task<string> IssueAsync(string telegramUserId, TimeSpan lifetime)
	{
		var code = string.Create(8, (string?)null, static (span, _) =>
		{
			for (var i = 0; i < span.Length; i++)
			{
				span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
			}
		});

		lock (_gate)
		{
			// Stryker disable Statement : чистка протухших кодов не видна снаружи, это защита от роста словаря
			DropExpired(DateTimeOffset.UtcNow);
			_codes[code] = (telegramUserId, DateTimeOffset.UtcNow.Add(lifetime));
		}

		return Task.FromResult(code);
	}

	public Task<string?> TakeAsync(string code)
	{
		lock (_gate)
		{
			DropExpired(DateTimeOffset.UtcNow);
			return Task.FromResult(_codes.Remove(code, out var entry) ? entry.UserId : null);
		}
	}

	private void DropExpired(DateTimeOffset now)
	{
		// Stryker disable Equality : граница <= vs < — дегенератный нулевой TTL, недоказуемо стабильным тестом
		foreach (var key in _codes.Where(kv => kv.Value.ExpiresAt <= now).Select(kv => kv.Key).ToArray())
		{
			_codes.Remove(key);
		}
	}
}
