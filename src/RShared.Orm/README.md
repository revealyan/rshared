# RShared.Orm

Контракты доступа к данным: репозиторий сущностей, фабрика репозиториев и unit of work.
Пакет — чистые интерфейсы без привязки к провайдеру; реализация поверх EF Core живёт в `RShared.Orm.EntityFrameworkCore`.

## Что внутри

- `IEntityRepository<TEntity>` — `InsertAsync`, `GetAsync(key)`, `AddAsync` (insert-or-update), `DeleteAsync`. Репозитории сами изменения не сохраняют
- `IEntityRepositoryFactory` — единственная точка входа: `Create<TEntity>()` для репозиториев и `CreateUnitOfWork(isolationLevel)` для транзакционной обвязки
- `IUnitOfWork` — чистый паттерн: `FlushAsync` (сохранить без коммита), `CommitAsync`, `RollbackAsync`, `IDisposable`

Контракты не тянут Queryable и прочих провайдерных вещей: запросы — прерогатива реализации (в EF-пакете это экстеншн `Query()`).

## Подключение

Сами по себе контракты ничего не регистрируют — берите пакет с реализацией:

```bash
dotnet add package RShared.Orm.EntityFrameworkCore
```

```csharp
builder.Services.AddEntityRepositories(typeof(AppDbContext), typeof(BillingDbContext));
```

Подробности — в README пакета `RShared.Orm.EntityFrameworkCore`.
