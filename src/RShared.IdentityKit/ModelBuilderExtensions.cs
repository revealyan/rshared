using Microsoft.EntityFrameworkCore;

namespace RShared.IdentityKit;

/// <summary>
/// EF model mapping for IdentityKit entities.
/// </summary>
public static class ModelBuilderExtensions
{
	/// <summary>
	/// Maps IdentityKit entities (TUser, ExternalAccount, OneTimeCode) into the calling context.
	/// Table names are pinned (users, external_accounts, one_time_codes) so consumer migrations
	/// do not depend on the consumer's entity class name. Call from OnModelCreating.
	/// Keys and required columns follow EF conventions (Id, non-nullable NRT properties).
	/// </summary>
	public static ModelBuilder ApplyIdentityKit<TUser>(this ModelBuilder modelBuilder)
		where TUser : IdentityKitUser
	{
		modelBuilder.Entity<TUser>(user =>
		{
			user.ToTable("users");
			user.HasIndex(u => u.Email).IsUnique();
		});

		modelBuilder.Entity<ExternalAccount>(account =>
		{
			account.ToTable("external_accounts");
			// Stryker disable once Statement : ссылочная целостность живёт в миграциях потребителя; SQLite в тестах FK не форсит, рантайм-эквивалент
			account.HasOne<TUser>().WithMany().HasForeignKey(a => a.UserId);
			account.HasIndex(a => new { a.Provider, a.ExternalId }).IsUnique();
		});

		modelBuilder.Entity<OneTimeCode>(code =>
		{
			code.ToTable("one_time_codes");
			// Stryker disable once Statement : формат хранения — строки читаемы в БД; EF транслирует обе стороны, поведение идентично
			code.Property(c => c.Purpose).HasConversion<string>();
			// Stryker disable once Statement : формат хранения — строки читаемы в БД; EF транслирует обе стороны, поведение идентично
			code.Property(c => c.Channel).HasConversion<string>();
			// Stryker disable once Statement : индекс для lookup-производительности, не поведения
			code.HasIndex(c => c.CodeHash);
		});

		return modelBuilder;
	}
}
