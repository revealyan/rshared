# RShared.IdentityKit

Лаконичная замена ASP.NET Identity поверх [RShared.AuthKit](../RShared.AuthKit) и [RShared.Orm](../RShared.Orm): юзеры, пароли, одноразовые коды, связки внешних аккаунтов, инвалидация сессий. Не надстройка над Identity — своя реализация с оглядкой на коробку как на каталог концептов; из платформы берём только `PasswordHasher`.

## Граница

- **AuthKit** — аутентификация: cookie-сессии, парольный вход, Google, TG-код, встроенная страница логина.
- **IdentityKit** — юзеры и всё, что вокруг них: таблицы `users` / `external_accounts` / `one_time_codes`, хэши паролей, email-коды (вход/подтверждение/сброс), роли-клеймы, security stamp.
- **Потребитель** — страница логина/регистрации/сброса (копируемые примеры ниже), доставка писем (`IEmailSender`), миграции, свои поля профиля (наследование юзера).

## Подключение

```csharp
// AppUser.cs — свои поля профиля через наследование
public sealed class AppUser : IdentityKitUser
{
	public string? DisplayName { get; set; }
}

// AppDbContext.cs — сущности в СВОЁМ контексте, имена таблиц прибиты
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
	protected override void OnModelCreating(ModelBuilder b) => b.ApplyIdentityKit<AppUser>();
}

// Program.cs
builder.Services.AddPostgreSqlRepositories(
	o => o.ConnectionString = builder.Configuration.GetConnectionString("db"),
	typeof(AppDbContext));
builder.Services.AddAuthKit(builder.Configuration.GetSection("authkit"));
builder.Services.AddIdentityKit<AppUser>(o =>
{
	o.CodeHashPepper = builder.Configuration["identitykit:pepper"]!;
});
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>(); // твоя доставка

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthKit();   // вместо MapRazorPages

// миграции — у потребителя:
// dotnet ef migrations add identitykit
```

Порядок `AddAuthKit`/`AddIdentityKit` неважен: EF-стор кодов сам снимает дефолтный in-memory AuthKit.
Забытый `ApplyIdentityKit` упадёт внятной ошибкой реестра Orm при первом резолве репозитория.

## Как работает

| фича | аналог в Identity | позиция |
|---|---|---|
| регистрация email+пароль, вход, смена | UserManager + SignInManager | взяли `PasswordHasher` из платформы; менеджеры — один сервис `IIdentityKit` |
| вход по Google / TG-код | внешние схемы + UserLogins | обобщили: резолвер AuthKit поверх таблицы `external_accounts` |
| вход по email-коду | нет из коробки (magic-link) | обобщили: одноразовый секрет, purpose=login |
| связки внешних аккаунтов | UserLogins | взяли концепт: `(provider, external_id)` уникален |
| link-by-email (A6) | не делает | опция `LinkByEmail`, выключена по умолчанию, только verified-провайдеры (Google) |
| подтверждение email / сброс пароля | ConfirmEmailAsync / ResetPasswordAsync | обобщили: тот же одноразовый секрет, другой purpose |
| инвалидация сессий | SecurityStamp + OnValidatePrincipal 30 мин | взяли концепт: reject-only, кэш `IMemoryCache` |
| блокировка юзера | LockoutEnd (путает с брутфорсом) | разделили: поле `enabled`, проверка на входе и в валидаторе |
| роли | Roles/UserRoles + RoleManager | выкинули менеджмент: список в колонке, role-клеймы в сессию |

Одноразовые коды: 8 знаков base32, в базе — только **HMAC-SHA256 с перцем** (hex), не сам код; поиск по хэшу индексный, при утечке базы коды не брутфорсятся (перец в секретах приложения). Повторная выдача той же тройке `(канал, purpose, назначение)` аннулирует предыдущий код. Потребление — атомарный условный UPDATE: гонка двух одновременных вводов оставляет одного ни с чем.

Сессия после любого входа: `NameIdentifier` = Guid юзера, `Name` = email, `Role`×N, `IdentityKitClaims.SecurityStamp`. Смена пароля/сброс регенерируют stamp — старые сессии режутся валидатором в пределах интервала кэша (30 мин по умолчанию), роли меняются со следующего входа.

## Использование

Регистрация + подтверждение, вход по коду:

```csharp
var result = await identityKit.RegisterAsync(model.Email, model.Password);
// код подтверждения уже улетел через IEmailSender

await identityKit.ConfirmEmailAsync(email, code);
await identityKit.SendEmailCodeAsync(email);      // молча, если аккаунта нет
var ok = await identityKit.EmailCodeSignInAsync(email, code);
```

Сброс пароля (не раскрывает существование email):

```csharp
await identityKit.RequestPasswordResetAsync(email);            // без результата
var ok = await identityKit.ResetPasswordAsync(email, code, newPassword);
```

Шов доставки — тексты полностью твои:

```csharp
public sealed class SmtpEmailSender : IEmailSender
{
	public Task SendCodeAsync(string to, OneTimeCodePurpose purpose, string code)
	{
		var subject = purpose switch
		{
			OneTimeCodePurpose.Login => "Код для входа",
			OneTimeCodePurpose.EmailConfirm => "Код подтверждения",
			OneTimeCodePurpose.PasswordReset => "Код сброса пароля",
			_ => "Код",
		};
		// отправка письма: subject + код
	}
}
```

Страницы (логин/регистрация/сброс) — тонкие обёртки над `IAuthKit`/`IIdentityKit`; логин уже есть у AuthKit (`/authkit/login`), свои страницы указываются через `loginPath`.

## Option

```csharp
new IdentityKitOption
{
	SessionScheme = AuthKitOption.DefaultScheme, // схема cookie AuthKit
	CodeHashPepper = "...",                     // обязателен, в секретах
	CodeLifetime = TimeSpan.FromMinutes(10),     // единый для всех purpose
	SessionLifetime = TimeSpan.FromDays(14),     // вход по email-коду
	SecurityStampValidationInterval = TimeSpan.FromMinutes(30),
	CreateUsersOnFirstExternalSignIn = true,     // первый внешний вход создаёт юзера
	LinkByEmail = false,                         // A6: линковка по verified email
}
```

## Ошибки

- `CodeHashPepper is required` — перец пуст; HMAC-lookup без перца не работает, fail-fast.
- `IdentityKit: IEmailSender is not registered` — вызван email-поток без доставки.
- `AuthKit: the telegram provider is disabled` и прочие — см. README AuthKit.

## Что осознанно выкинули из ASP.NET Identity

2FA/TOTP/recovery-коды; телефон/SMS; менеджмент ролей (RoleManager); брутфорс-счётчики AccessFailedCount/LockoutEnd — включи ASP.NET Core rate limiting (`AddRateLimiter`) на пути логина и выдачи кодов; `NormalizedEmail`/`NormalizedUserName`-дубли (email хранится канонически: trim + lower invariant); ConcurrencyStamp (оптимистичная конкуренция); двухтабличные Roles/UserRoles; таблицы UserClaims/UserTokens (клеймы — шов потребителя, токены → одноразовые секреты); Identity UI (готовые страницы); rehash при `SuccessRehashNeeded` (валидация — читающий шов, запись потребовала бы UoW).

## Ограничения

- Перец обязателен; ротация перца сжигает активные коды (не пароли) — задокументировано, v1 без ротации.
- Stamp-кэш per-node: инвалидация в пределах 30 минут на узел; смена ролей действует со следующего входа.
- Повторная блокировка→разблокировка оживляет старую сессию, пока жив cookie.
- Один активный код на тройку (канал, purpose, назначение).

## Регистрации

Всё `TryAdd` — потребитель перебивает своим до вызова:

| регистрация | lifetime | что даёт |
|---|---|---|
| `IdentityKitOption` | singleton | сконфигурированный option |
| `IPasswordHasher<TUser>` → `PasswordHasher<TUser>` | singleton | PBKDF2 из shared framework |
| `IAuthKitPasswordStore` → `EfPasswordStore<TUser>` | scoped | шов AuthKit: проверка пары |
| `IAuthKitUserResolver` → `EfUserResolver<TUser>` | scoped | шов AuthKit: внешний id → юзер |
| `IOneTimeCodeStore` → `EfOneTimeCodeStore` | scoped | коды в БД (снимает memory-дефолт AuthKit) |
| `IIdentityKit` → `IdentityKitService<TUser>` | scoped | потоки юзера |
| `SecurityStampValidator<TUser>` (internal) | scoped | валидация сессий |
| `PostConfigure<CookieAuthenticationOptions>` | — | крючок OnValidatePrincipal на схеме AuthKit |
| `IEmailSender` | — | НЕ регистрируется пакетом — шов потребителя |
