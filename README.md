# rshared

Библиотеки-рельсы для проектов revealyan: каждая самостоятельна, минимум зависимостей, подключение одной строкой в хост.

## Пакеты

| пакет | что делает |
|---|---|
| `RShared.AuthKit` | готовая аутентификация: пароль, Google OAuth, одноразовые коды Telegram, cookie-сессии, встроенная страница логина (можно свои страницы) |
| `RShared.Mediator` | минимальный mediator: сообщение → хендлер |
| `RShared.Orm` | контракты хранилища: репозиторий сущностей, фабрика, unit of work |
| `RShared.Orm.EntityFrameworkCore` | реализация контрактов Orm на EF Core |
| `RShared.Orm.PostgreSql` | коннектор Orm-стека к PostgreSQL: провайдер Npgsql, общий пул, snake_case, подключение одной строкой |
| `RShared.RabbitMq` | шина поверх RabbitMQ: publisher/consumer адаптеры, JSON-сериализация |

## Подключение из GitHub Packages

Пакеты приватные, чтение требует токен (classic PAT со скоупом `read:packages`). В проекте-потребителе рядом с решением кладётся `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/revealyan/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="revealyan" />
      <add key="ClearTextPassword" value="%GITHUB_PACKAGES_TOKEN%" />
    </github>
  </packageSourceCredentials>
</configuration>
```

`GITHUB_PACKAGES_TOKEN` — переменная окружения с PAT (в CI удобно `GITHUB_TOKEN`, у него уже есть `read:packages` в своём репо). Дальше обычный `PackageReference`:

```xml
<PackageReference Include="RShared.AuthKit" Version="0.0.3" />
```

## Публикация

Версия общая на все пакеты. `major.minor` живёт в `version.json` и правится обычным МР; патч присваивается автоматически — последняя опубликованная в фиде версия серии + 1.

Релиз = мердж в main: workflow `publish` собирает пакеты, вычисляет следующую патч-версию и пушит все `.nupkg` в GitHub Packages (`--skip-duplicate`, так что повторный запуск не уедет поверх опубликованной версии — он опубликует следующую).

Минор/мажор: в МР правится `version.json` (например, `"0.1"` → `"0.2"`) — первый мердж после этого опубликует `0.2.0`. Прямые пуш-релизы через теги не используются.
