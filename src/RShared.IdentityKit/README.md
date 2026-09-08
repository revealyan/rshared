# RShared.IdentityKit

Готовая аутентификация и пользователи для проектов revealyan — своя лаконичная замена ASP.NET Identity. Два слоя в одном пакете:

- **аутентификация** — cookie-сессии, вход по паролю / Google / TG-коду / email-коду;
- **пользователи** — таблицы `users` / `external_accounts` / `one_time_codes` поверх RShared.Orm: регистрация, подтверждение email, сброс пароля, роли-клеймы, инвалидация сессий, встроенные страницы.

Свой подход с оглядкой на коробку как на каталог концептов; из платформы берём только `PasswordHasher`.

## Граница

Слой аутентификации доказывает, что человек владеет внешним аккаунтом, и выдаёт сессию. Слой пользователей решает, кто такой юзер и что он может. Потребитель делает остальное:

```
AddAuthKit                 аутентификация: сессии, пароль/Google/TG, страница логина
AddIdentityKit<TUser>      юзеры: EF-реализации швов, коды email, stamp-валидатор
потребитель                IEmailSender (доставка), миграции, поля профиля (наследование TUser)
```

Швы (потребитель может перебить своими до вызова `Add*`):

```
IAuthKitPasswordStore      проверка пароля (дефолт: EfPasswordStore + PasswordHasher)
IAuthKitUserResolver       внешний id → юзер приложения (дефолт: EfUserResolver)
IOneTimeCodeStore          одноразовые коды (дефолт: EF-стор; чистый AddAuthKit — память процесса)
IEmailSender               доставка email-кодов — всегда потребитель
```

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
	o.CodeHashPepper = builder.Configuration["authkit:pepper"]!;
});
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthKit();   // встроенные страницы + MapRazorPages

// миграции — у потребителя: dotnet ef migrations add authkit
```

```json
// appsettings.json — провайдеры включаются непустой секцией (пустой объект {} не включает)
{
  "authkit": {
    "loginPageTitle": "Sign in",
    "password":  { "sessionLifetime": "14.00:00:00" },
    "google":    { "clientId": "...", "clientSecret": "...", "sessionLifetime": "30.00:00:00" },
    "telegram":  { "codeLifetime": "00:10:00" }
  }
}
```

Порядок `AddAuthKit`/`AddIdentityKit` неважен: EF-стор кодов сам снимает дефолтный in-memory.
Забытый `ApplyIdentityKit` упадёт внятной ошибкой реестра Orm при первом резолве репозитория.

## Встроенные страницы

| путь | что делает |
|---|---|
| `/authkit/login` | пароль / TG-код / Google; ссылки на регистрацию и сброс |
| `/authkit/register` | создание аккаунта + отправка кода подтверждения + автологин |
| `/authkit/confirm` | ввод кода подтверждения email |
| `/authkit/forgot` | запрос кода сброса (сообщение нейтрально — не раскрывает существование email) |
| `/authkit/reset` | код + новый пароль; старые сессии режутся ротацией stamp |

### Свои страницы вместо встроенных

Встроенные страницы — тонкие обёртки (образец: `Pages/Login.cshtml` в исходниках пакета);
всё, что они делают, доступно из кода. Подставить свои — три правила:

1. Своя страница = обычная Razor-страница на **своём пути** (`/account/register`, что угодно),
   которая инжектит `IAuthKit` / `IIdentityKit` и зовёт те же методы (примеры ниже).
   Путь должен отличаться от `/authkit/*` — два разаряда на одном маршруте это конфликт роутинга.
2. Редиректы «на логин» настраиваются `loginPath` в `AuthKitOption` — укажи свой путь,
   и валидатор, и страницы сброса/подтверждения будут вести туда.
3. Встроенные страницы при этом остаются достижимы по прямому URL — их нельзя отключить,
   можно просто не ссылаться на них (ссылки живут только на самих встроенных страницах).
   Полностью «убрать» — не ссылаться и закрыть пути rate limiter'ом при желании.

Типовой сценарий: свои регистрация и логин (брендированные, с капчей) + встроенные
confirm/forgot/reset как есть — достаточно `loginPath` и ссылок на `/authkit/*` из своих страниц.

## Использование

Регистрация → подтверждение → вход по коду:

```csharp
var result = await identityKit.RegisterAsync(model.Email, model.Password);
if (result.Status == RegisterStatus.DuplicateEmail) { /* показать ошибку */ }

// код подтверждения уже улетел через IEmailSender
await identityKit.ConfirmEmailAsync(email, code);

await identityKit.SendEmailCodeAsync(email);                 // молча, если аккаунта нет
var ok = await identityKit.EmailCodeSignInAsync(email, code);
```

Сброс пароля (не раскрывает существование email):

```csharp
await identityKit.RequestPasswordResetAsync(email);          // без результата
var ok = await identityKit.ResetPasswordAsync(email, code, newPassword);
```

Смена пароля и блокировка:

```csharp
await identityKit.ChangePasswordAsync(userId, current, newPassword);
await identityKit.SetEnabledAsync(userId, false);
```

Коды Telegram доставляет бот потребителя (у пакета своей связи с TG нет):

```csharp
// обработчик апдейтов бота (команда /login):
var code = await authKit.IssueTelegramCodeAsync(message.From.Id);
await bot.SendMessage(message.Chat.Id, $"Код для входа: {code}");
```

Доставка email — шов потребителя, тексты полностью твои:

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
		return Task.CompletedTask;
	}
}
```

## Провайдеры входа

| провайдер | как работает | что нужно потребителю |
|---|---|---|
| password | `IAuthKitPasswordStore.ValidateAsync` → резолвер → сессия | ничего (EF-реализация в пакете) |
| google | OAuth → `OnTicketReceived` → резолвер → сессия; scope `email` запрошен | clientId/secret, callback `/authkit/google-callback` |
| telegram | бот потребителя просит код (`IAuthKit.IssueTelegramCodeAsync`) и доставляет юзеру | бот у потребителя |
| email-код | `IIdentityKit.SendEmailCodeAsync` → код → `EmailCodeSignInAsync` | `IEmailSender` |

## Пользователи

| фича | аналог в Identity | позиция |
|---|---|---|
| регистрация, вход, смена пароля | UserManager + SignInManager | взяли `PasswordHasher`; менеджеры — один сервис `IIdentityKit` |
| связки внешних аккаунтов | UserLogins | взяли концепт: `(provider, external_id)` уникален |
| link-by-email | не делает | опция `LinkByEmail`, выключена по умолчанию, только verified-провайдеры |
| подтверждение email / сброс пароля | ConfirmEmailAsync / ResetPasswordAsync | обобщили: одноразовый секрет с purpose |
| инвалидация сессий | SecurityStamp + OnValidatePrincipal 30 мин | взяли концепт: reject-only, кэш `IMemoryCache` |
| блокировка юзера | LockoutEnd (путает с брутфорсом) | разделили: поле `enabled` |
| роли | Roles/UserRoles + RoleManager | выкинули менеджмент: список в колонке, role-клеймы в сессию |

Одноразовые коды: 8 знаков base32, в базе — только **HMAC-SHA256 с перцем**, не сам код;
повторная выдача той же тройке `(канал, purpose, назначение)` аннулирует предыдущий; потребление —
атомарный условный UPDATE. Клеймы сессии: `NameIdentifier`, `Name`, `Role`×N,
`IdentityKitClaims.SecurityStamp`. Смена/сброс пароля регенерируют stamp — старые сессии режутся
в пределах интервала кэша (30 мин), роли меняются со следующего входа.

## Option

```csharp
new AuthKitOption          // слой аутентификации
{
	Scheme = AuthKitOption.DefaultScheme,     // имя cookie-схемы
	LoginPath = "/authkit/login",
	DefaultReturnPath = "/",
	Password = new PasswordOption(),           // null выключает
	Google = null,                             // секция конфига включает
	Telegram = new TelegramOption(),
}

new IdentityKitOption       // слой пользователей
{
	SessionScheme = AuthKitOption.DefaultScheme,
	CodeHashPepper = "...",                   // обязателен, в секретах
	CodeLifetime = TimeSpan.FromMinutes(10),
	SessionLifetime = TimeSpan.FromDays(14),
	SecurityStampValidationInterval = TimeSpan.FromMinutes(30),
	CreateUsersOnFirstExternalSignIn = true,
	LinkByEmail = false,
}
```

## Ошибки

- `CodeHashPepper is required` — перец пуст; HMAC-lookup без перца не работает, fail-fast.
- `AuthKit: IEmailSender is not registered` — email-поток без доставки.
- `AuthKit: google provider is enabled, but clientId/clientSecret are empty` — кривая секция конфига.
- `AuthKit: the telegram provider is disabled` — `IssueTelegramCodeAsync` при выключенном провайдере.

## Что осознанно выкинули из ASP.NET Identity

2FA/TOTP/recovery-коды; телефон/SMS; менеджмент ролей; брутфорс-счётчики (включи ASP.NET Core
rate limiting `AddRateLimiter` на пути логина и выдачи кодов); NormalizedEmail-дубли (email
хранится канонически: trim + lower); ConcurrencyStamp; двухтабличные Roles/UserRoles;
UserClaims/UserTokens-таблицы; Identity UI как обязательный слой (готовые страницы есть, но всё
доступно из кода); rehash при `SuccessRehashNeeded`.

## Ограничения

- Перец обязателен; ротация сжигает активные коды (не пароли).
- Stamp-кэш per-node: инвалидация в пределах 30 минут на узел.
- Блокировка→разблокировка оживляет старую сессию, пока жив cookie.
- Один активный код на тройку (канал, purpose, назначение).
- AuthKit не троттлит попытки входа — rate limiting на хосте.

## Регистрации

Всё `TryAdd` — потребитель перебивает своим до вызова:

| регистрация | lifetime | что даёт |
|---|---|---|
| `AuthKitOption` / `IdentityKitOption` | singleton | сконфигурированные option |
| `IAuthKit` → `AuthKitService` | scoped | вход/выход/челленджи |
| cookie-схема + Google-схема | — | сессии и OAuth |
| `IPasswordHasher<TUser>` → `PasswordHasher<TUser>` | singleton | PBKDF2 из shared framework |
| `IAuthKitPasswordStore` → `EfPasswordStore<TUser>` | scoped | проверка пары |
| `IAuthKitUserResolver` → `EfUserResolver<TUser>` | scoped | внешний id → юзер |
| `IOneTimeCodeStore` → `EfOneTimeCodeStore` | scoped | коды в БД |
| `IIdentityKit` → `IdentityKitService<TUser>` | scoped | потоки юзера |
| `SecurityStampValidator<TUser>` (internal) | scoped | валидация сессий |
| `PostConfigure<CookieAuthenticationOptions>` | — | крючок OnValidatePrincipal |
| `IEmailSender` | — | НЕ регистрируется пакетом — шов потребителя |
