using Microsoft.EntityFrameworkCore;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Контекст потребителя: сущности IdentityKit в своей модели
/// </summary>
internal sealed class KitContext(DbContextOptions<KitContext> options) : DbContext(options)
{
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.ApplyIdentityKit<IdentityKitUser>();
	}
}
