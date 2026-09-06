using System.Security.Cryptography;

namespace RShared.AuthKit;

/// <summary>
/// Single use Telegram codes with a TTL, kept in process memory.
/// Enough for a single node; register another <see cref="ITgCodeStore"/> implementation
/// (Redis, a database table) for multi node farms.
/// </summary>
public sealed class MemoryTgCodeStore : ITgCodeStore
{
	private readonly object _gate = new();
	private readonly Dictionary<string, (string UserId, DateTimeOffset ExpiresAt)> _codes = [];

	public Task<string> IssueAsync(string telegramUserId, TimeSpan lifetime)
	{
		// six digits: enough for a short lived single use code, no ambiguous characters
		var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
		lock (_gate)
		{
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
		foreach (var key in _codes.Where(kv => kv.Value.ExpiresAt <= now).Select(kv => kv.Key).ToArray())
		{
			_codes.Remove(key);
		}
	}
}
