using Microsoft.EntityFrameworkCore;

namespace RShared.Orm.EntityFrameworkCore.Tests;

public sealed class Order
{
	public int Id { get; set; }

	public decimal Amount { get; set; }
}

public sealed class Payment
{
	public int Id { get; set; }

	public int OrderId { get; set; }
}

public sealed class CatalogContext
	: DbContext
{
	public CatalogContext(DbContextOptions<CatalogContext> options)
		: base(options)
	{
	}

	public DbSet<Order> Orders => Set<Order>();
}

public sealed class BillingContext
	: DbContext
{
	public BillingContext(DbContextOptions<BillingContext> options)
		: base(options)
	{
	}

	public DbSet<Payment> Payments => Set<Payment>();
}

/// <summary>
/// Второй контекст с той же сущностью Order — для проверки конфликта владения
/// </summary>
public sealed class ConflictContext
	: DbContext
{
	public ConflictContext(DbContextOptions<ConflictContext> options)
		: base(options)
	{
	}

	public DbSet<Order> Orders => Set<Order>();
}

/// <summary>
/// Контекст, зарегистрированный в фабрике репозиториев, но не в DI
/// </summary>
public sealed class GhostContext
	: DbContext
{
	public GhostContext(DbContextOptions<GhostContext> options)
		: base(options)
	{
	}

	public DbSet<Payment> Payments => Set<Payment>();
}

/// <summary>
/// Сущность, которой не владеет ни один контекст
/// </summary>
public sealed class UnknownEntity { }

/// <summary>
/// Абстрактный контекст — регистрация должна отвергать
/// </summary>
public abstract class AbstractContext
	: DbContext
{
	public AbstractContext(DbContextOptions<AbstractContext> options)
		: base(options)
	{
	}
}
