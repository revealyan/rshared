namespace RShared.AuthKit;

/// <summary>
/// Consumer seam: validates a login and a password pair against the consumer storage.
/// AuthKit never stores passwords.
/// </summary>
public interface IAuthKitPasswordStore
{
	Task<bool> ValidateAsync(string login, string password);
}
