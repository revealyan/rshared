using Xunit;
using RShared.IdentityKit;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Хранилище одноразовых кодов: формат, одноразовость, TTL,
/// изоляция purpose/канала/назначения, аннулирование при повторной выдаче
/// </summary>
public sealed class MemoryOneTimeCodeStoreTests
{
	private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

	[Fact]
	public async Task Issue_returns_code_with_base32_format()
	{
		var store = new MemoryOneTimeCodeStore();

		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.Equal(8, code.Length);
		Assert.All(code, symbol => Assert.Contains(symbol, Alphabet));
	}

	[Fact]
	public async Task Take_returns_destination_and_burns_code()
	{
		var store = new MemoryOneTimeCodeStore();
		var code = await store.IssueAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "123", TimeSpan.FromMinutes(10));

		Assert.Equal("123", await store.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, code));
		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, code));
	}

	[Fact]
	public async Task Take_unknown_code_returns_null()
	{
		var store = new MemoryOneTimeCodeStore();

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "ZZZZZZZZ"));
	}

	[Fact]
	public async Task Take_drops_expired_code()
	{
		var store = new MemoryOneTimeCodeStore();
		var code = await store.IssueAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "123", TimeSpan.Zero);

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, code));
	}

	[Fact]
	public async Task Take_with_destination_drops_expired_code()
	{
		var store = new MemoryOneTimeCodeStore();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.Zero);

		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", code));
	}

	[Fact]
	public async Task Issue_produces_unique_codes()
	{
		var store = new MemoryOneTimeCodeStore();
		var codes = new HashSet<string>();

		for (var i = 0; i < 100; i++)
		{
			codes.Add(await store.IssueAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "123", TimeSpan.FromMinutes(10)));
		}

		Assert.Equal(100, codes.Count);
	}

	[Fact]
	public async Task Take_does_not_take_code_of_other_purpose()
	{
		var store = new MemoryOneTimeCodeStore();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.PasswordReset, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, code));
	}

	[Fact]
	public async Task Take_does_not_take_code_of_other_channel()
	{
		var store = new MemoryOneTimeCodeStore();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, code));
	}

	[Fact]
	public async Task Take_with_destination_matches_full_triple_and_burns_code()
	{
		var store = new MemoryOneTimeCodeStore();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.True(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", code));
		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", code));
	}

	[Fact]
	public async Task Take_with_destination_rejects_other_destination()
	{
		var store = new MemoryOneTimeCodeStore();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "other@b.c", code));
	}

	[Fact]
	public async Task Take_with_destination_rejects_other_purpose()
	{
		var store = new MemoryOneTimeCodeStore();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.PasswordReset, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", code));
	}

	[Fact]
	public async Task Reissue_annuls_previous_code_of_same_triple()
	{
		var store = new MemoryOneTimeCodeStore();
		var first = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));
		var second = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, first));
		Assert.Equal("a@b.c", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, second));
	}

	[Fact]
	public async Task Codes_of_different_destinations_are_independent()
	{
		var store = new MemoryOneTimeCodeStore();
		var first = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));
		var second = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "x@y.z", TimeSpan.FromMinutes(10));

		Assert.Equal("a@b.c", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, first));
		Assert.Equal("x@y.z", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, second));
	}

	[Fact]
	public async Task Codes_of_same_destination_in_other_channel_or_purpose_are_independent()
	{
		var store = new MemoryOneTimeCodeStore();
		var email = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "shared", TimeSpan.FromMinutes(10));
		var reset = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.PasswordReset, "shared", TimeSpan.FromMinutes(10));
		var telegram = await store.IssueAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "shared", TimeSpan.FromMinutes(10));

		// выдача одного не аннулирует коды той же тройки с другим каналом или purpose
		Assert.Equal("shared", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, email));
		Assert.Equal("shared", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.PasswordReset, reset));
		Assert.Equal("shared", await store.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, telegram));
	}
}
