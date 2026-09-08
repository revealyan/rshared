using System.Security.Cryptography;

namespace RShared.AuthKit;

/// <summary>
/// One time code generator shared by the store implementations.
/// </summary>
public static class OneTimeCodeGenerator
{
	// base32 без похожих символов (нет 0/1/I/L/O/U): 8 знаков ≈ 10^12 комбинаций,
	// перебор снаружи безо всякого rate limit становится бессмысленным
	private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

	/// <summary>
	/// Generates a new code.
	/// </summary>
	public static string Generate()
	{
		return string.Create(8, (string?)null, static (span, _) =>
		{
			for (var i = 0; i < span.Length; i++)
			{
				span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
			}
		});
	}
}
