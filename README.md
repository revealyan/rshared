# rshared

[![CI](https://github.com/revealyan/rshared/actions/workflows/ci.yml/badge.svg)](https://github.com/revealyan/rshared/actions/workflows/ci.yml)
[![RShared.IdentityKit](https://img.shields.io/nuget/v/RShared.IdentityKit)](https://www.nuget.org/packages/RShared.IdentityKit)
[![RShared.Mediator](https://img.shields.io/nuget/v/RShared.Mediator)](https://www.nuget.org/packages/RShared.Mediator)
[![RShared.Orm](https://img.shields.io/nuget/v/RShared.Orm)](https://www.nuget.org/packages/RShared.Orm)
[![RShared.Orm.EntityFrameworkCore](https://img.shields.io/nuget/v/RShared.Orm.EntityFrameworkCore)](https://www.nuget.org/packages/RShared.Orm.EntityFrameworkCore)
[![RShared.Orm.PostgreSql](https://img.shields.io/nuget/v/RShared.Orm.PostgreSql)](https://www.nuget.org/packages/RShared.Orm.PostgreSql)
[![RShared.RabbitMq](https://img.shields.io/nuget/v/RShared.RabbitMq)](https://www.nuget.org/packages/RShared.RabbitMq)

Библиотеки-рельсы для проектов revealyan: каждая самостоятельна, минимум зависимостей, подключение одной строкой в хост.

## Пакеты

| пакет | что делает |
|---|---|
| `RShared.IdentityKit` | аутентификация и пользователи: пароль, Google OAuth, одноразовые коды Telegram/email, cookie-сессии, регистрация, подтверждение, сброс пароля, роли, инвалидация сессий, встроенные страницы |
| `RShared.Mediator` | минимальный mediator: сообщение → хендлер |
| `RShared.Orm` | контракты хранилища: репозиторий сущностей, фабрика, unit of work |
| `RShared.Orm.EntityFrameworkCore` | реализация контрактов Orm на EF Core |
| `RShared.Orm.PostgreSql` | коннектор Orm-стека к PostgreSQL: провайдер Npgsql, общий пул, snake_case, подключение одной строкой |
| `RShared.RabbitMq` | шина поверх RabbitMQ: типизированные хендлеры, ретраи с DLQ, prefetch, publisher confirms, привязка очередей в composition root |

## Подключение

Пакеты публикуются в публичный [nuget.org](https://www.nuget.org/packages?q=RShared) — обычный `PackageReference`, ничего настраивать не надо:

```xml
<PackageReference Include="RShared.IdentityKit" Version="10.0.0" />
```

Приватное зеркало в GitHub Packages — для внутренних потребителей; чтение требует токен
(classic PAT со скоупом `read:packages`). В проекте-потребителе рядом с решением кладётся `nuget.config`:

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

`GITHUB_PACKAGES_TOKEN` — переменная окружения с PAT (в CI удобно `GITHUB_TOKEN`, у него уже есть `read:packages` в своём репо).

## Публикация

Версия общая на все пакеты. `major.minor` живёт в `version.json` и правится обычным МР; патч присваивается автоматически — последняя опубликованная в фиде версия серии + 1.

Релиз = мердж в main (или в `release/*`): workflow `publish` собирает пакеты, вычисляет следующую
патч-версию серии и пушит все `.nupkg` в GitHub Packages и, если задан секрет `NUGET_API_KEY`,
на публичный nuget.org (`--skip-duplicate` в обоих случаях, так что повторный запуск не уедет
поверх опубликованной версии — он опубликует следующую).

Минор/мажор: в МР правится `version.json` (например, `"10.0"` → `"10.1"`) — первый мердж после
этого опубликует `10.1.0`. Серии выровнены по мажору платформы (net10.0 → 10.x). Прямые
пуш-релизы через теги не используются.
