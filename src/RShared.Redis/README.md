# RShared.Redis

Рельса поверх StackExchange.Redis: подключение одной строкой, свои лаконичные концепты — потребитель не видит типы клиента. Кэш, pub/sub, streams, локи, rate limiter.

## Что умеет Redis и что из этого взяли

| возможность | решение | где |
|---|---|---|
| строки + TTL | **взяли** | `IRedisCache` |
| типизированный кэш (JSON) | **взяли** | `IRedisCache.Get<T>/Set<T>` |
| get-or-set с конкурентным промахом | **взяли** (SET NX + GET; вычисление под локом — опция `SingleFlight`) | `GetOrSetAsync` |
| атомарный счётчик с TTL | **взяли** (Lua INCRBY+PEXPIRE первой установки) | `IncrementAsync` |
| удаление по паттерну | **взяли** (SCAN курсорами + батчами, никогда KEYS) | `DeleteByPatternAsync` |
| pub/sub | **взяли** (ephemeral: fire-and-forget, реконнект мультиплексором) | `IRedisPublisher` + `AddRedisSubscription` |
| streams с consumer groups | **взяли** (XADD/XREADGROUP/XACK/XAUTOCLAIM, at-least-once, DLQ-стрим; RabbitMq не обязателен) | `IRedisStreamPublisher` + `AddRedisStreamHandler` |
| распределённые локи | **взяли** (SET NX + TTL, release/extend Lua по токену) | `IRedisLock` |
| rate limiting | **взяли** (fixed INCR+PEXPIRE; sliding ZSET+TIME сервера) | `IRedisRateLimiter` |
| IDistributedCache | **взяли** адаптером поверх нашего мультиплексора (для сессий и сторонних либ) | `AddRedisDistributedCache` |
| keyspace notifications | **взяли** (expired/del/evicted, с проверкой конфига сервера) | `AddRedisKeyEvents` |
| доступ к структурам (hash/list/set/zset/…) | **дверь** `IRedisNative` вместо обёрток | осознанный выход из концептов |
| streams как exactly-once | **выкинули** | честный at-least-once, хендлеры идемпотентны |
| watchdog автопродления локов | **выкинули из v1** | ручной `ExtendAsync`; продление на 1/3 TTL |
| KEYS (одномоментный обход) | **выкинули** | блокирует сервер |

## Подключение

```csharp
builder.Services.AddRedis(builder.Configuration.GetSection("redis"));
```

```json
{ "redis": { "connectionString": "localhost:6379", "keyPrefix": "app:", "channelPrefix": "app-" } }
```

Один мультиплексор на приложение; готовый `IConnectionMultiplexer` — через `o.Connection` (владение потребителя, пакет не диспозит). `ConfigureConnection` — эскейп-хэтк к `ConfigurationOptions` (таймауты, ssl, retry). `AbortOnConnectFail=false` по умолчанию: хост не валится без редиса, соединение восстановится. `KeyPrefix` — всем ключам (кэш, локи, лимиты, стримы), `ChannelPrefix` — каналам pub/sub.

## Кэш

```csharp
await cache.SetAsync("user:1", new User("Alice"), TimeSpan.FromMinutes(5));
var user = await cache.GetAsync<User>("user:1");

// конкурентный промах: хранение и возврат — один победитель NX; вычисление — под локом
var heavy = await cache.GetOrSetAsync("report:q4", ct => BuildReportAsync(ct),
	TimeSpan.FromMinutes(10), new RedisGetOrSetOption { SingleFlight = true });

await cache.IncrementAsync("hits", delta: 1, timeToLive: TimeSpan.FromDays(1));

// SCAN-курсорами по всему keyspace: не блокирует, но O(ключей)
await cache.DeleteByPatternAsync("app:report:*");
```

## Pub/sub (ephemeral)

```csharp
builder.Services.AddRedisSubscription<OrderPaid>(e => Console.WriteLine($"paid {e.Amount}"));

await publisher.PublishAsync(new OrderPaid(42));   // канал = {ChannelPrefix}OrderPaid
```

Сообщения fire-and-forget: подписчик должен быть жив в момент публикации, иначе сообщение потеряно навсегда. Poison-тело и упавший хендлер роняют только своё сообщение, подписка жива. Нужна доставка — streams или [RShared.RabbitMq](../RShared.RabbitMq).

## Streams (durable)

```csharp
builder.Services.AddRedisStreamHandler<OrderPaid>(async e => await ShipAsync(e), o =>
{
	o.Group = "workers";                  // одна группа = балансировка, разные = fan-out
	o.MaxRetryCount = 3;                  // ретраи в процессе
	o.DeadLetterStream = ".dlq";          // {stream}.dlq; "" — только лог
	o.MinIdleBeforeReclaim = TimeSpan.FromMinutes(1);  // XAUTOCLAIM зависших
	o.MaxStreamLength = 100_000;          // тримминг XADD MAXLEN ~ (0 — никогда)
});

await streamPublisher.PublishAsync(new OrderPaid(42));
```

Группа создаётся с начала стрима: сообщения до первого старта не теряются. At-least-once — после краша запись вернётся reclaim'ом (счёт попыток заново), хендлеры идемпотентны. Требует Redis ≥ 6.2 (XAUTOCLAIM).

## Локи

```csharp
await using var lease = await locks.TryAcquireAsync("index-rebuild", TimeSpan.FromMinutes(10));
if (lease is null) return;                        // занято кем-то другим

await lease.ExtendAsync(TimeSpan.FromMinutes(10)); // продлить (false — лок уже не твой)

var waited = await locks.WaitAsync("job", TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30)); // опрос 100 мс
```

TTL подбирай с запасом: истёкший при живом держателе лок освободится и его возьмёт другой — release по токеню защищает только от снятия чужого. Продление — на 1/3 TTL для длинных работ (watchdog — v2).

## Rate limiter

```csharp
// брутфорс-защита логина (выкинутый C4 IdentityKit): sliding — без burst на стыке окон
var result = await limiter.AllowAsync("login:" + email, limit: 5, window: TimeSpan.FromMinutes(1), RedisRateWindow.Sliding);
if (!result.Allowed) return TooManyRequests(result.RetryAfter);

// грубые квоты: fixed — дешевле (INCR + PEXPIRE), но 2x burst на стыке
var quota = await limiter.AllowAsync("api:" + clientId, 1000, TimeSpan.FromHours(1));
```

Оба окна — атомарные Lua; sliding берёт часы из `TIME` сервера — клиентский дрифт не влияет. Не смешивай режимы на одном ключе.

## Совместимость

```csharp
builder.Services.AddRedis(...).AddRedisDistributedCache();  // IDistributedCache поверх НАШЕГО мультиплексора
builder.Services.AddSession();                              // сессии едят IDistributedCache
```

Отличие от коробочного `AddStackExchangeRedisCache`: одно соединение на всё приложение (коробка поднимает свой мультиплексор). Sliding-записи живут парой ключей (`{key}` + `{key}:s` с исходным окном), `RefreshAsync` продлевает оба. Регистрация снимает чужую memory-реализацию (AddSession успевает поставить свою).

## События ключей

```csharp
builder.Services.AddRedisKeyEvents(e => CleanupAsync(e.Key), o => o.Events = [RedisKeyEventKind.Expired]);
```

Требует `CONFIG SET notify-keyspace-events "Ex"` на сервере. Fire-and-forget — не источник истины.

## Дверь к структурам данных

```csharp
var db = native.Database;   // сырой IDatabase: hash/list/set/zset/bitmap/geo
```

Разовые экзотики — через `IRedisNative` без обёрток; повторяющийся паттерн из проекта в проект — кандидат в концепт.

## Интеграция с IdentityKit

[Redis.IdentityKit](../RShared.Redis.IdentityKit) — мостик: одноразовые коды и stamp-кэш в Redis, мульти-нодный IdentityKit.

## Интеграционные тесты

Юнит-тесты пакета гоняются на моках без сервера; интеграционные включаются env `REDIS_CONNECTION` (CI поднимает service container `redis:7-alpine`, локально — докер или свой сервер).

## Ограничения

- Ephemeral pub/sub: потеря сообщений при мёртвом подписчике — семантика, не баг.
- Sliding-лимитер: O(limit) памяти и O(log limit) на вызов — дороже fixed.
- DeleteByPattern: O(keyspace) обход, best effort на момент скана.
- Локи и время: решает таймер Redis; TTL с запасом, продление вручную.
- Стримы: at-least-once, дубликаты возможны; рестарт процесса начинает счёт ретраев заново.
- IDistributedCache sliding: две записи на ключ (side-key).

## Регистрации

Всё `TryAdd` — перебивай своим до вызова:

| регистрация | lifetime | что даёт |
|---|---|---|
| `RedisOption` | singleton | сконфигурированный option |
| `IRedisCache` | singleton | кэш (строки/типы/GetOrSet/инкремент/паттерн) |
| `IRedisPublisher` | singleton | ephemeral pub/sub |
| `IRedisStreamPublisher` | singleton | XADD с триммингом |
| `IRedisLock` | singleton | локи |
| `IRedisRateLimiter` | singleton | окна |
| `IRedisNative` | singleton | сырой `IDatabase` |
| `IHostedService` ×2 | singleton | подписки pub/sub + стрим-консюмер (через `AddRedis*`) |
| `IDistributedCache` | singleton | только после `AddRedisDistributedCache` (снимает memory) |
