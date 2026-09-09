using System.Security.Cryptography;
using System.Text;
using RShared.IdentityKit;

namespace RShared.Redis.IdentityKit;

/// <summary>
/// One time codes in Redis over the package concepts (IRedisCache + IRedisLock, no Lua here):
/// only HMAC-SHA256(pepper, code) hashes are stored; a new code of a triple annuls the previous
/// one; consumption deletes both keys under the triple lock — atomic for our writers.
/// </summary>
public sealed class RedisOneTimeCodeStore(
	IRedisCache cache,
	IRedisLock locks,
	IdentityKitOption option) : IOneTimeCodeStore
{
	/// <inheritdoc />
	public async Task<string> IssueAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string destination, TimeSpan lifetime)
	{
		var code = OneTimeCodeGenerator.Generate();
		var hash = Hash(code);
		var triple = Triple(channel, purpose, destination);

		// короткое ожидание: Issue конфликтует только с другими Issue той же тройки
		await using var lease = await locks.WaitAsync("otc:" + triple, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
		if (lease is null)
		{
			// лок не взяли — перезапишем пару как есть: безопасное направление (старый код гаснет)
			await WritePairAsync(triple, hash, destination, lifetime);
			return code;
		}

		// аннулируем предыдущий код тройки: прямой ключ перезапишется, обратный надо погасить
		var previousHash = await cache.GetAsync(Forward(triple));
		if (previousHash is not null && previousHash != hash)
		{
			await cache.DeleteAsync(Reverse(previousHash));
		}

		await WritePairAsync(triple, hash, destination, lifetime);
		return code;
	}

	/// <inheritdoc />
	public async Task<string?> TakeAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string code)
	{
		var hash = Hash(code);

		await using var lease = await locks.TryAcquireAsync("otc-code:" + hash, TimeSpan.FromSeconds(10));
		if (lease is null)
		{
			// кто-то держит лок на этот код — считаем код невалидным (безопасное направление)
			return null;
		}

		var destination = await cache.GetAsync(Reverse(hash));
		if (destination is null)
		{
			return null;
		}

		// точка одноразовости: обратный ключ гасим сразу; прямой чистим, если он всё ещё наш
		await cache.DeleteAsync(Reverse(hash));
		var forward = await cache.GetAsync(Forward(Triple(channel, purpose, destination)));
		if (forward == hash)
		{
			await cache.DeleteAsync(Forward(Triple(channel, purpose, destination)));
		}

		return destination;
	}

	/// <inheritdoc />
	public async Task<bool> TakeAsync(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string destination, string code)
	{
		var triple = Triple(channel, purpose, destination);
		var hash = Hash(code);

		await using var lease = await locks.TryAcquireAsync("otc:" + triple, TimeSpan.FromSeconds(10));
		if (lease is null)
		{
			return false;
		}

		var stored = await cache.GetAsync(Forward(triple));
		if (stored != hash)
		{
			return false;
		}

		await cache.DeleteAsync(Forward(triple));
		await cache.DeleteAsync(Reverse(hash));
		return true;
	}

	private async Task WritePairAsync(string triple, string hash, string destination, TimeSpan lifetime)
	{
		await cache.SetAsync(Forward(triple), hash, lifetime);
		await cache.SetAsync(Reverse(hash), destination, lifetime);
	}

	private static string Triple(OneTimeCodeChannel channel, OneTimeCodePurpose purpose, string destination)
	{
		return $"{channel}:{purpose}:{destination}";
	}

	private static string Forward(string triple)
	{
		return "otc:" + triple;
	}

	private static string Reverse(string hash)
	{
		return "otc-code:" + hash;
	}

	private string Hash(string code)
	{
		var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(option.CodeHashPepper), Encoding.UTF8.GetBytes(code));
		return Convert.ToHexString(mac);
	}
}
