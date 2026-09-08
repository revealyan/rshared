namespace RShared.IdentityKit;

/// <summary>
/// Security stamp generation.
/// </summary>
internal static class Stamp
{
	/// <summary>
	/// A fresh opaque stamp; any change of it invalidates live sessions.
	/// </summary>
	// Stryker disable once String : пустой формат GUID эквивалентен "N", поведение не меняется
	public static string New()
	{
		return Guid.NewGuid().ToString("N");
	}
}
