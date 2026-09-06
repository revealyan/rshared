# rshared

Библиотеки-рельсы для проектов revealyan: каждая самостоятельна, минимум зависимостей, подключение одной строкой в хост.

## Пакеты

| пакет | что делает |
|---|---|
| `RShared.AuthKit` | готовая аутентификация: пароль, Google OAuth, одноразовые коды Telegram, cookie-сессии, встроенная страница логина (можно свои страницы) |
| `RShared.Mediator` | минимальный mediator: сообщение → хендлер |
| `RShared.Orm` | контракты хранилища: репозиторий сущностей, фабрика, unit of work |
| `RShared.Orm.EntityFrameworkCore` | реализация контрактов Orm на EF Core |
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

Версия общая на все пакеты — в `Directory.Build.props`. Релиз:

```
git tag v0.0.3 && git push origin v0.0.3
```

Тег запускает workflow: `dotnet pack RShared.slnx -c Release` → push всех `.nupkg` в GitHub Packages (`--skip-duplicate`, так что неперебитая версия не уедет).
