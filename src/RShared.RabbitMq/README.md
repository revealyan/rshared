# RShared.RabbitMq

Шина поверх RabbitMQ: типизированные хендлеры, publisher с confirms, ретраи с dead-letter, prefetch, консюмеры как hosted-сервис. Привязка «очередь ↔ хендлер» живёт в composition root — бизнес-слой знает только контракт `IRabbitMqHandler<T>`, в конфиге — только строка подключения.

## Подключение

Инфраструктура — в composition root (инфра или api-слой):

```csharp
builder.Services.AddRabbitMq(options => options.ConnectionString =
	builder.Configuration.GetConnectionString("rabbit"));
```

Хендлер — чистый бизнес-класс без знания об очередях:

```csharp
public sealed class OrderCreatedHandler
	: IRabbitMqHandler<OrderCreated>
{
	public Task HandleAsync(OrderCreated message, CancellationToken cancellationToken = default)
	{
		// ...
		return Task.CompletedTask;
	}
}
```

Привязка хендлера к очереди — там же, в composition root:

```csharp
builder.Services.AddRabbitMq(options => { options.ConnectionString = "..."; })
	.AddRabbitMqHandler<OrderCreatedHandler, OrderCreated>("orders-created", topology =>
	{
		topology.Exchange = "shop";
		topology.MaxRetryCount = 5;
	});
```

Публикация — в default exchange, routing key = имя очереди:

```csharp
await publisher.PublishAsync("orders-created", new OrderCreated { OrderId = 42 });
```

Хендлеры регистрируются как scoped: каждая доставка получает свой скоуп (репозитории, unit of work работают как обычно).

## Топология

`AddRabbitMqHandler` объявляет очередь при старте хоста:

| свойство | дефолт | что значит |
|---|---|---|
| `queueName` | — | имя очереди, уникально в приложении |
| `Exchange` | default | exchange для привязки (direct) |
| `RoutingKey` | имя очереди | ключ привязки |
| `MaxRetryCount` | `3` | перепоставки упавшего сообщения до dead-letter |
| `DeadLetterQueue` | `{queue}.dlq` | куда падают отвергнутые; пустая строка — выключить |
| `Durable` | `true` | durable-очереди |

## Обработка сбоев

- хендлер отработал → `ack`;
- хендлер упал → `nack(requeue)` — сообщение вернётся сразу же, без задержки; счётчик попыток в памяти по `MessageId`, после `MaxRetryCount` неудач → `nack(reject)`;
- reject уводит сообщение в dead-letter очередь (`{queue}.dlq`), если она не выключена;
- тело не десериализуется (poison) → сразу reject, без ретраев;
- у сообщения нет `MessageId` (паблишили не мы) → любая ошибка сразу reject, ретраев нет;
- счётчик попыток живёт в памяти: рестарт процесса начинает счёт заново — кап только ограничивает ретраи, не продлевает.

## Доставка

- prefetch (`basicQos`) настраивается `PrefetchCount` (дефолт 16) на каждый консюмер-канал;
- publisher — своё соединение и канал, `PublisherConfirms` (дефолт вкл): `PublishAsync` завершается только после подтверждения брокера;
- `PersistentMessages` (дефолт вкл) — persistent delivery mode;
- консюмеры стартуют hosted-сервисом на старте хоста, останавливаются с graceful shutdown: незавершённые доставки не подтверждаются и вернутся после рестарта.

## Что выкинули из старой версии (0.0.x)

Адаптеры (`IRabbitMqConsumerAdapter`/`IRabbitMqPublisherAdapter`), `RabbitMqEventWrapper` с двойной сериализацией, конфигурация очередей из appsettings, ack-в-finally (терял сообщения при ошибках), атрибут-скан очередей на бизнес-классах (привязка теперь явная, в composition root). Мажор сломает API — потребителей старой версии нет.
