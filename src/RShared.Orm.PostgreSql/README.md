# RShared.Orm.PostgreSql

Коннектор стека `RShared.Orm.EntityFrameworkCore` к PostgreSQL: провайдер Npgsql, общий пул соединений, snake_case-нейминг — подключение хоста одной строкой.

## Что делает

- `UseNpgsql` + общий `NpgsqlDataSource`: пул один на процесс, шарится всеми контекстами (из `ConnectionString` строится автоматически, либо передаётся готовый через `DataSource`);
- snake_case таблиц и колонок через `EFCore.NamingConventions` — включён по умолчанию, отключается `UseSnakeCaseNaming = false`;
- регистрация контекстов + фабрики репозиториев (`AddEntityRepositories`) одним вызовом;
- готовый `NpgsqlDataSource` попадает в DI — для сырых запросов поверх того же пула.

## Подключение

```csharp
builder.Services.AddPostgreSqlRepositories(
	options => options.ConnectionString = builder.Configuration.GetConnectionString("Database"),
	typeof(CatalogContext), typeof(BillingContext));
```

Все контексты получают один data source и общие настройки. После этого доступен весь стек: `IEntityRepositoryFactory`, `CreateUnitOfWork`, ambient unit of work — см. README `RShared.Orm.EntityFrameworkCore`.

Один контекст без репозиториев или со своими настройками:

```csharp
builder.Services.AddPostgreSqlContext<CatalogContext>(options =>
{
	options.ConnectionString = "...";
	options.ConfigureNpgsql = npgsql => npgsql.CommandTimeout(30);
});
```

Готовый `NpgsqlDataSource` (например, со своими настройками билдера):

```csharp
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

builder.Services.AddPostgreSqlRepositories(
	options => options.DataSource = dataSource,
	typeof(CatalogContext));
```

## Секреты

Библиотека ничего не знает про секрет-хранилища (Vault, Key Vault и т.п.) — поставка секретов целиком на стороне приложения, либа получает уже готовые значения:

- статический секрет (переменная окружения, файл от sidecar-агента, конфиг) — приложение собирает строку подключения и передаёт в `ConnectionString`;
- ротация / динамические креды — приложение строит `NpgsqlDataSource` само с колбэком пароля (`NpgsqlDataSourceBuilder.UsePasswordProvider` — Vault дёргается при каждом открытии соединения) и передаёт его через `DataSource`.

Второй вариант уже покрывает всё, что нужно для интеграции с хранилищами — волт-клиент внутрь пакета не потащим.

## Ретраи

`EnableRetryOnFailure` по умолчанию выключен: ретраящая стратегия исполнения EF несовместима с явными транзакциями, а unit of work их открывает. Включай только для контекстов, которые живут без unit of work.

## Зачем отдельный пакет

Провайдер Npgsql — осознанная зависимость только для тех, кто на PostgreSQL: ядро ORM-стека остаётся провайдер-нейтральным (и не тянет Npgsql транзитивно), версия провайдера запинена в одном месте — здесь.
