namespace RShared.AuthKit;

/// <summary>
/// AuthKit dependency injection configuration options.
/// AuthKit is authentication only: it proves that a person owns an external account
/// (password login, Google OAuth, Telegram one-time code) and issues a cookie session.
/// Application user storage, roles and permissions stay on the consumer side.
/// </summary>
public class AuthKitOption
{
	/// <summary>
	/// Default cookie authentication scheme used for issued sessions.
	/// Packages building on top of AuthKit hook into the scheme by this name.
	/// </summary>
	public const string DefaultScheme = "RShared.AuthKit";

	/// <summary>
	/// Cookie authentication scheme used for issued sessions.
	/// </summary>
	public string Scheme { get; set; } = DefaultScheme;

	/// <summary>
	/// Path AuthKit redirects to when a session is required.
	/// Points either to the built-in login page or to a consumer page.
	/// </summary>
	public string LoginPath { get; set; } = "/authkit/login";

	/// <summary>
	/// Where to redirect after a successful sign in.
	/// </summary>
	public string DefaultReturnPath { get; set; } = "/";

	/// <summary>
	/// Title shown on the built-in login page.
	/// </summary>
	public string LoginPageTitle { get; set; } = "Sign in";

	/// <summary>
	/// Password login. Requires an <see cref="IAuthKitPasswordStore"/> implementation.
	/// Enabled by default, set <c>Password = null</c> to disable.
	/// </summary>
	public PasswordOption? Password { get; set; } = new();

	/// <summary>
	/// Google OAuth. <c>null</c> (default) disables the provider.
	/// </summary>
	public GoogleOption? Google { get; set; }

	/// <summary>
	/// Telegram login by one-time code: the consumer bot asks AuthKit for a code
	/// and delivers it to the user, the user enters the code on the login page.
	/// Enabled by default, set <c>Telegram = null</c> to disable.
	/// </summary>
	public TelegramOption? Telegram { get; set; } = new();
}

/// <summary>
/// Password provider options
/// </summary>
public class PasswordOption
{
	/// <summary>
	/// Lifetime of the issued session for password sign ins
	/// </summary>
	public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);
}

/// <summary>
/// Google OAuth provider options
/// </summary>
public class GoogleOption
{
	/// <summary>
	/// Google OAuth client id. An empty value disables the provider.
	/// </summary>
	public string ClientId { get; set; } = string.Empty;

	/// <summary>
	/// Google OAuth client secret
	/// </summary>
	public string ClientSecret { get; set; } = string.Empty;

	/// <summary>
	/// OAuth callback path, must match the Google application configuration
	/// </summary>
	public string CallbackPath { get; set; } = "/authkit/google-callback";

	/// <summary>
	/// Lifetime of the issued session for Google sign ins
	/// </summary>
	public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);
}

/// <summary>
/// Telegram provider options
/// </summary>
public class TelegramOption
{
	/// <summary>
	/// Lifetime of a one-time code
	/// </summary>
	public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

	/// <summary>
	/// Lifetime of the issued session for Telegram sign ins
	/// </summary>
	public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);
}
