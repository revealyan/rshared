# RShared.Redis.IdentityKit

Мостик между [RShared.Redis](../RShared.Redis) и [RShared.IdentityKit](../RShared.IdentityKit): одноразовые коды и кэш security-штампов в Redis. Мульти-нодный IdentityKit одной строкой: коды общие на все узлы, инвалидация сессий мгновенная и глобальная.

## Подключение

```csharp
builder.AddRedis(o => o.ConnectionString = builder.Configuration.GetConnectionString("redis"))
	.AddRedisIdentityKit()            // ПОСЛЕ AddRedis, ДО AddAuthKit/AddIdentityKit
	.AddAuthKit(builder.Configuration.GetSection("authkit"))
	.AddIdentityKit<AppUser>(o => o.CodeHashPepper = builder.Configuration["authkit:pepper"]!);
```

## Как работает

- **`RedisOneTimeCodeStore`** — реализация шва `IOneTimeCodeStore`: в Redis лежат только HMAC-SHA256(перец, код)-хэши (`otc:{канал}:{purpose}:{назначение}` → хэш, `otc-code:{хэш}` → назначение); повторная выдача тройки гасит предыдущий код; потребление атомарно под локом тройки — писатели кодов все наши, лок их сериализует. Промах лока = код невалиден (безопасное направление).
- **`RedisSecurityStampCache`** — реализация шва `ISecurityStampCache` поверх `IRedisCache`: смена/сброс пароля дропает штамп из Redis — все узлы режут сессию сразу, а не в пределах 30-минутного окна на узел.

## Регистрации

Обе `TryAdd` — вызывай до `AddAuthKit`/`AddIdentityKit`, тогда их memory/EF-дефолты не встанут:

| регистрация | реализация | что даёт |
|---|---|---|
| `IOneTimeCodeStore` | `RedisOneTimeCodeStore` | одноразовые коды в Redis |
| `ISecurityStampCache` | `RedisSecurityStampCache` | глобальный кэш штампов |

Без `AddRedis` — `ArgumentException` (fail-fast). Ключи наследуют `KeyPrefix` из `RedisOption`.
