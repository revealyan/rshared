using Xunit;
using RShared.AuthKit;

namespace RShared.AuthKit.Tests;

/// <summary>
/// Хранилище одноразовых кодов: формат, одноразовость, TTL
/// </summary>
public sealed class MemoryTgCodeStoreTests
{
	private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

	[Fact]
	public async Task Issue_returns_code_with_base32_format()
	{
		var store = new MemoryTgCodeStore();

		var code = await store.IssueAsync("123", TimeSpan.FromMinutes(10));

		Assert.Equal(8, code.Length);
		Assert.All(code, symbol => Assert.Contains(symbol, Alphabet));
	}

	[Fact]
	public async Task Take_returns_user_and_burns_code()
	{
		var store = new MemoryTgCodeStore();
		var code = await store.IssueAsync("123", TimeSpan.FromMinutes(10));

		Assert.Equal("123", await store.TakeAsync(code));
		Assert.Null(await store.TakeAsync(code));
	}

	[Fact]
	public async Task Take_unknown_code_returns_null()
	{
		var store = new MemoryTgCodeStore();

		Assert.Null(await store.TakeAsync("ZZZZZZZZ"));
	}

	[Fact]
	public async Task Take_drops_expired_code()
	{
		var store = new MemoryTgCodeStore();
		var code = await store.IssueAsync("123", TimeSpan.Zero);

		Assert.Null(await store.TakeAsync(code));
	}

	[Fact]
	public async Task Issue_produces_unique_codes()
	{
		var store = new MemoryTgCodeStore();
		var codes = new HashSet<string>();

		for (var i = 0; i < 100; i++)
		{
			codes.Add(await store.IssueAsync("123", TimeSpan.FromMinutes(10)));
		}

		Assert.Equal(100, codes.Count);
	}
}
