# RShared.AuthKit

Готовая аутентификация для проектов revealyan: пароль, Google OAuth, вход по одноразовому коду Telegram + cookie-сессии + встроенная страница логина (можно свои).

## Граница

AuthKit — **только аутентификация**: он доказывает, что человек владеет внешним аккаунтом (логин+пароль, Google, Telegram), и выдаёт сессию. Хранилище юзеров, роли и права остаются на стороне потребителя — через два шва:

```
IAuthKitPasswordStore   проверка пароля (пароли хранит потребитель, не AuthKit)
IAuthKitUserResolver    внешний id → юзер приложения (найти/создать/отказать)
```

AuthKit не тянет EF/Identity в зависимости. Провайдер включается присутствием секции конфига.

## Подключение

```csharp
// Program.cs
builder.Services.AddAuthKit(builder.Configuration.GetSection("authkit"));
// швы потребителя
builder.Services.AddScoped<IAuthKitUserResolver, MyUserResolver>();
builder.Services.AddScoped<IAuthKitPasswordStore, MyPasswordStore>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthKit();   // вместо MapRazorPages: встроенная страница /authkit/login
```

```json
// appsettings.json — провайдеры включаются присутствием секций
{
  "authkit": {
    "loginPath": "/authkit/login",
    "password":  {},
    "google":    { "clientId": "...", "clientSecret": "..." },
    "telegram":  { "codeLifetimeMinutes": 10 }
  }
}
```

Сессия после входа — claim `ClaimTypes.NameIdentifier` = id юзера из резолвера.

## Провайдеры

| провайдер | как работает | что нужно потребителю |
|---|---|---|
| password | `IAuthKitPasswordStore.ValidateAsync` → сессия | хранилище паролей |
| google | OAuth → `OnTicketReceived` → резолвер → сессия | clientId/secret, callback `/authkit/google-callback` в консоли Google |
| telegram | бот потребителя просит код (`IAuthKit.IssueTelegramCodeAsync`) и доставляет юзеру; код вводится на странице | бот у потребителя (у AuthKit своей связи с TG нет) |

## Вход через Telegram

AuthKit не подключается к Telegram — код выдаётся по API, доставляет его бот потребителя:

```csharp
// обработчик апдейтов бота (команда /login):
var code = await authKit.IssueTelegramCodeAsync(message.From.Id);
await bot.SendMessage(message.Chat.Id, $"Код для входа: {code}");
```

Код шестизначный, одноразовый, с TTL (по умолчанию 10 минут). Введённый на странице логина код создаёт сессию с `ExternalIdentity("telegram", <id>)`.

## Свои страницы вместо встроенной

Логика входа живёт в `IAuthKit`, страница тонкая. Свой UI = свои Razor-страницы, дёргающие `PasswordSignInAsync` / `TelegramSignInAsync` / `ChallengeGoogleAsync`, а `loginPath` в конфиге указывает на свой путь.

## Ограничения скелета

- коды Telegram по умолчанию в памяти процесса (`MemoryTgCodeStore`) — для много-нодового деплоя зарегистрировать свою реализацию `ITgCodeStore` до `AddAuthKit`;
- сессия Google — фикс 14 дней;
- Apple не заведён (нужен платный dev-аккаунт и ревью) — добавить по мере надобности.
