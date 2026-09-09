using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RShared.IdentityKit;
using Xunit;

namespace RShared.Redis.IdentityKit.Tests;

/// <summary>
/// Мостик: стор кодов (хэши, аннулирование, одноразовость, изоляция purpose/назначения,
/// промах лока) и stamp-кэш поверх концептов ядра, регистрации
/// </summary>
public sealed class BridgeTests
{
	private readonly IRedisCache _cache = Substitute.For<IRedisCache>();
	private readonly IRedisLock _locks = Substitute.For<IRedisLock>();
	private readonly IdentityKitOption _option = new() { CodeHashPepper = "pepper" };

	private RedisOneTimeCodeStore Build()
	{
		return new RedisOneTimeCodeStore(_cache, _locks, _option);
	}

	private Task<RedisLease?> LeaseAcquired()
	{
		// RedisLease sealed — тестовая пара ключ/токен; release уйдёт в null-базу, но мостик его не вызывает
		var lease = new RedisLease(Substitute.For<StackExchange.Redis.IDatabase>(), "test", "test");
		_locks.WaitAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(lease);
		_locks.TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(lease);
		return Task.FromResult<RedisLease?>(lease);
	}

	[Fact]
	public async Task Issue_stores_only_hashes_with_ttl()
	{
		await LeaseAcquired();
		_cache.GetAsync(Arg.Any<string>()).Returns((string?)null);
		var store = Build();

		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		// лок — по тройке
		await _locks.Received(1).WaitAsync("otc:Email:Login:a@b.c", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), Arg.Any<CancellationToken>());
		// предыдущего кода нет — гасить нечего
		await _cache.DidNotReceiveWithAnyArgs().DeleteAsync(Arg.Any<string>());

		// прямой ключ: тройка → HMAC кода (не сам код); обратный: HMAC → назначение
		await _cache.Received(1).SetAsync(
			Arg.Is<string>(k => k == "otc:Email:Login:a@b.c"),
			Arg.Is<string>(v => v != code && v.Length == 64),
			TimeSpan.FromMinutes(10));
		await _cache.Received(1).SetAsync(
			Arg.Is<string>(k => k.StartsWith("otc-code:") && k.Length == "otc-code:".Length + 64),
			"a@b.c",
			TimeSpan.FromMinutes(10));
	}

	[Fact]
	public async Task Issue_annuls_the_previous_code_of_the_triple()
	{
		await LeaseAcquired();
		var previous = new string('A', 64);
		_cache.GetAsync("otc:Email:Login:a@b.c").Returns(previous);
		var store = Build();

		await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		await _cache.Received(1).DeleteAsync("otc-code:" + previous);
	}

	[Fact]
	public async Task Take_by_triple_matches_and_burns()
	{
		await LeaseAcquired();
		var store = Build();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));
		var hash = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
			"pepper"u8.ToArray(), System.Text.Encoding.UTF8.GetBytes(code)));
		_cache.ClearReceivedCalls();
		_cache.GetAsync("otc:Email:Login:a@b.c").Returns(hash);
		await LeaseAcquired();

		Assert.True(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", code));

		// лок — по тройке
		await _locks.Received(1).TryAcquireAsync("otc:Email:Login:a@b.c", TimeSpan.FromSeconds(10), Arg.Any<CancellationToken>());
		await _cache.Received(1).DeleteAsync("otc:Email:Login:a@b.c");
		await _cache.Received(1).DeleteAsync("otc-code:" + hash);

		// одноразовость: прямого ключа больше нет
		_cache.GetAsync("otc:Email:Login:a@b.c").Returns((string?)null);
		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", code));
	}

	[Fact]
	public async Task Take_by_code_returns_destination()
	{
		await LeaseAcquired();
		var store = Build();
		var code = await store.IssueAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "123", TimeSpan.FromMinutes(10));
		var hash = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
			"pepper"u8.ToArray(), System.Text.Encoding.UTF8.GetBytes(code)));
		_cache.ClearReceivedCalls();
		_cache.GetAsync("otc-code:" + hash).Returns("123");
		await LeaseAcquired();

		Assert.Equal("123", await store.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, code));
		await _cache.Received(1).DeleteAsync("otc-code:" + hash);

		// лок — по хэшу кода
		await _locks.Received(1).TryAcquireAsync("otc-code:" + hash, TimeSpan.FromSeconds(10), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Take_with_unknown_code_returns_null()
	{
		await LeaseAcquired();
		var store = Build();
		var hash = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
			"pepper"u8.ToArray(), System.Text.Encoding.UTF8.GetBytes("ZZZZZZZZ")));
		_cache.GetAsync("otc-code:" + hash).Returns((string?)null);

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "ZZZZZZZZ"));
		await _cache.DidNotReceiveWithAnyArgs().DeleteAsync(Arg.Any<string>());
	}

	[Fact]
	public async Task Take_lock_miss_is_a_rejection()
	{
		_locks.TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns((RedisLease?)null);
		var store = Build();

		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", "ZZZZZZZZ"));
		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "ZZZZZZZZ"));
	}

	[Fact]
	public async Task Take_by_code_keeps_the_forward_key_of_a_newer_issue()
	{
		await LeaseAcquired();
		var store = Build();
		var hash = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
			"pepper"u8.ToArray(), System.Text.Encoding.UTF8.GetBytes("ABCD2345")));
		_cache.GetAsync("otc-code:" + hash).Returns("a@b.c");
		// прямой ключ уже перезаписан новым Issue — это не наш код
		_cache.GetAsync("otc:Email:Login:a@b.c").Returns((string?)"newer-hash");

		Assert.Equal("a@b.c", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "ABCD2345"));
		await _cache.DidNotReceive().DeleteAsync("otc:Email:Login:a@b.c");
	}

	[Fact]
	public void Stamp_cache_maps_to_keys()
	{
		var cache = Substitute.For<IRedisCache>();
		var stamps = new RedisSecurityStampCache(cache);
		var userId = Guid.NewGuid();

		stamps.GetAsync(userId);
		stamps.SetAsync(userId, "s1", TimeSpan.FromMinutes(30));
		stamps.InvalidateAsync(userId);

		cache.Received(1).GetAsync("stamp:" + userId);
		cache.Received(1).SetAsync("stamp:" + userId, "s1", TimeSpan.FromMinutes(30));
		cache.Received(1).DeleteAsync("stamp:" + userId);
	}

	[Fact]
	public void Registration_requires_add_redis_first()
	{
		var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRedisIdentityKit());

		Assert.Contains("AddRedis", exception.Message);
	}

	[Fact]
	public void Registration_wires_both_seams()
	{
		var services = new ServiceCollection();
		services.AddRedis(o => o.ConnectionString = "localhost:6379");
		services.AddRedisIdentityKit();
		// стору нужен IdentityKitOption реального IdentityKit-хоста
		services.AddSingleton(new IdentityKitOption { CodeHashPepper = "p" });

		var provider = services.BuildServiceProvider();

		// оба шва TryAdd-ятся поверх AddRedis
		Assert.IsType<RedisOneTimeCodeStore>(provider.GetRequiredService<IOneTimeCodeStore>());
		Assert.IsType<RedisSecurityStampCache>(provider.GetRequiredService<ISecurityStampCache>());
		// builder-перегрузка регистрирует то же
		var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
		builder.Services.AddRedis(o => o.ConnectionString = "localhost:6379");
		builder.Services.AddSingleton(new IdentityKitOption { CodeHashPepper = "p" });
		builder.AddRedisIdentityKit();
		Assert.IsType<RedisOneTimeCodeStore>(builder.Services.BuildServiceProvider().GetRequiredService<IOneTimeCodeStore>());
	}

	[Fact]
	public async Task Issue_lock_miss_writes_the_pair_anyway()
	{
		_locks.WaitAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns((RedisLease?)null);
		var store = Build();

		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		// безопасное направление: пару записали, старый код перезаписан
		await _cache.Received(1).SetAsync(Arg.Is<string>(k => k == "otc:Email:Login:a@b.c"), Arg.Any<string>(), TimeSpan.FromMinutes(10));
		Assert.Equal(8, code.Length);
	}

	[Fact]
	public async Task Take_by_code_removes_the_forward_key_of_its_own_triple()
	{
		await LeaseAcquired();
		var store = Build();
		var hash = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
			"pepper"u8.ToArray(), System.Text.Encoding.UTF8.GetBytes("ABCD2345")));
		_cache.GetAsync("otc-code:" + hash).Returns("a@b.c");
		_cache.GetAsync("otc:Email:Login:a@b.c").Returns(hash);

		await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "ABCD2345");

		// прямой ключ — наш: сгорает вместе с обратным
		await _cache.Received(1).DeleteAsync("otc:Email:Login:a@b.c");
	}
}
