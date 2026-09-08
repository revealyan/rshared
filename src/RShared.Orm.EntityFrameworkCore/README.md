# RShared.Orm.EntityFrameworkCore

Реализация контрактов `RShared.Orm` поверх EF Core: репозитории по типу сущности и unit of work по нескольким контекстам.

## Как работает

Владение сущностью определяется моделью контекста — что контекст сконфигурировал (DbSet / OnModelCreating), тем он и владеет.
Никаких отдельных реестров: завели DbSet — сущность уже «зарегана».

Подключение — одна строчка со списком контекстов:

```csharp
builder.Services.AddEntityRepositories(typeof(AppDbContext), typeof(BillingDbContext));
```

Контексты резолвятся по типу: если в DI зарегистрирована `IDbContextFactory<T>` — контексты создаются через неё (и фабрика репозиториев сама их диспозит), иначе контекст берётся из скоупа.
Резолверы контекстов готовятся один раз при регистрации; карта владения прогревается при старте хоста (hosted service) — конфликт владельцев валит приложение на старте, а не в первом реквесте. Без хоста карта строится при первом обращении. В рантайме запроса — только словарь и делегат, без рефлексии и прохода по моделям.

## Использование

Репозитории — только через фабрику, unit of work — только транзакционная обвязка:

```csharp
public class CreateOrderHandler(
	IEntityRepositoryFactory repositoryFactory)
{
	public async Task HandleAsync(CreateOrder command, CancellationToken cancellationToken)
	{
		await using var unitOfWork = repositoryFactory.CreateUnitOfWork();

		var orders = repositoryFactory.Create<Order>();
		var users = repositoryFactory.Create<User>();

		await orders.InsertAsync(new Order(command.UserId), cancellationToken);
		await users.AddAsync(command.User, cancellationToken);

		await unitOfWork.CommitAsync(cancellationToken);
	}
}
```

`Create<TEntity>()` сам находит контекст-владелец: `Order` уедет в `AppDbContext`, `Payment` — в `BillingDbContext`, без ручной привязки.

## Транзакции

Транзакция открывается лениво: репозиторий при **первой мутации** (Insert/Add/Delete) регистрирует свой контекст в активном unit of work, и транзакция начинается только там.
Контекст, из которого только читали, транзакции не получает; пустой `CreateUnitOfWork()` в базу не ходит.
`FlushAsync` — SaveChanges по затронутым контекстам, `CommitAsync` — flush + коммит, `RollbackAsync` — откат по затронутым.
Завершение скоупа (откат, dispose без коммита, отпускание после коммита) чистит ChangeTracker затронутых контекстов — контекст остаётся чистым для следующего unit of work того же скоупа.

Мутации до первого unit of work в скоупе никуда не сохраняются сами — репозитории `SaveChanges` не зовут, изменения подвисают в ChangeTracker до ближайшего flush этого контекста. Мутации после завершённого unit of work — исключение: скоуп мёртв, нужен новый.

**Вложенность.** `CreateUnitOfWork()` при активном внешнем не падает — новый unit of work присоединяется к нему, это один общий скоуп:

- изоляция — корневого;
- `CommitAsync` вложенного — чекпоинт: flush в общую транзакцию без фиксации;
- `CommitAsync` корневого — настоящий `COMMIT` всего скоупа;
- `RollbackAsync` любого — откат всего скоупа: точечный откат «только внутренней части» невозможен, изменения в контексте общие;
- после Commit/Rollback скоуп завершён — новые мутации и flush бросают `InvalidOperationException`;
- корень, диспознутый раньше вложенных, откатывает скоуп и кидает `InvalidOperationException` — закоммитить его уже некому.

Транзакции коммитятся последовательно, без 2PC: если коммит упал посередине, ранние контексты останутся закоммиченными.
Для атомарности между контекстами в пределах одной БД держите их в одном DbContext.

## Запросы (LINQ)

`Query()` не входит в контракты — `IQueryable` несёт ожидания провайдера. Для EF это экстеншн-метод сбоку:

```csharp
var orders = repositoryFactory.Create<Order>().Query();
var bigOrders = await orders
	.Where(order => order.Amount > 100)
	.ToListAsync(cancellationToken);
```

На не-EF репозиториях метод бросает `NotSupportedException`.

## Ошибки

Владение проверяется при прогреве карты (старт хоста либо первое обращение) и падает с внятным текстом:

- сущность в моделях двух контекстов — `InvalidOperationException` с именами обоих;
- сущность не в одной модели — `InvalidOperationException` с подсказкой добавить её в модель контекста;
- контекст не зарегистрирован в DI (ни фабрика, ни сам контекст) — `NotSupportedException`.

## Регистрации

`AddEntityRepositories` использует `TryAdd*` — свои `IEntityRepositoryFactory` или `EntityRepositoryRegistry` можно перебить до вызова.
