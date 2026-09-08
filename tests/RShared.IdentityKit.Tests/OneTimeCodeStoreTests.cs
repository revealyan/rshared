using Microsoft.EntityFrameworkCore;
using RShared.IdentityKit;

using Xunit;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// EF-стор кодов: в базе хэш, а не код; одноразовость, TTL,
/// изоляция purpose/канала/назначения, аннулирование при повторной выдаче
/// </summary>
public sealed class OneTimeCodeStoreTests
{
	[Fact]
	public async Task Database_stores_the_hash_not_the_code()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var code = await scope.Get<IOneTimeCodeStore>().IssueAsync(
			OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		var row = await scope.Get<KitContext>().Set<OneTimeCode>().SingleAsync();

		Assert.NotEqual(code, row.CodeHash);
		Assert.Equal(64, row.CodeHash.Length);
		Assert.Null(row.ConsumedAt);
	}

	[Fact]
	public async Task Take_with_destination_is_single_use()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IOneTimeCodeStore>();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.True(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", code));
		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", code));
	}

	[Fact]
	public async Task Take_returns_destination_and_is_single_use()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IOneTimeCodeStore>();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.Equal("a@b.c", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, code));
		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, code));
	}

	[Fact]
	public async Task Unknown_code_is_rejected()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IOneTimeCodeStore>();

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "ZZZZZZZZ"));
		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", "ZZZZZZZZ"));
	}

	[Fact]
	public async Task Expired_code_is_rejected()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IOneTimeCodeStore>();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromSeconds(-1));

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, code));
	}

	[Fact]
	public async Task Code_of_other_purpose_never_satisfies_take()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IOneTimeCodeStore>();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.PasswordReset, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, code));
		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", code));
	}

	[Fact]
	public async Task Code_of_other_channel_never_satisfies_take()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IOneTimeCodeStore>();
		var code = await store.IssueAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "123", TimeSpan.FromMinutes(10));

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, code));
	}

	[Fact]
	public async Task Take_with_destination_requires_the_same_destination()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IOneTimeCodeStore>();
		var code = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.False(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "other@b.c", code));
	}

	[Fact]
	public async Task Reissue_annuls_the_previous_code_of_the_same_triple()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IOneTimeCodeStore>();
		var first = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));
		var second = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, first));
		Assert.Equal("a@b.c", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, second));
	}

	[Fact]
	public async Task Reissue_does_not_annul_codes_of_other_triples()
	{
		using var fixture = IdentityKitFixture.Create();
		using var scope = fixture.OpenScope();
		var store = scope.Get<IOneTimeCodeStore>();

		var login = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));
		var reset = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.PasswordReset, "a@b.c", TimeSpan.FromMinutes(10));
		var telegram = await store.IssueAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, "123", TimeSpan.FromMinutes(10));
		var fresh = await store.IssueAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, "a@b.c", TimeSpan.FromMinutes(10));

		// аннулирован только код той же тройки (login); reset и telegram живут своей жизнью
		Assert.Null(await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, login));
		Assert.Equal("a@b.c", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.PasswordReset, reset));
		Assert.Equal("123", await store.TakeAsync(OneTimeCodeChannel.Telegram, OneTimeCodePurpose.Login, telegram));
		Assert.Equal("a@b.c", await store.TakeAsync(OneTimeCodeChannel.Email, OneTimeCodePurpose.Login, fresh));
	}
}
