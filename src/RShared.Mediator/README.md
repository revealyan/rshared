# RShared.Mediator

Минимальный mediator: одно сообщение — один хендлер. Без пайплайнов, мидлварей и фан-аута — только точечный диспетч через DI.

## Как работает

- Хендлеры регистрируются по закрытым интерфейсам `IMessageHandler<TMessage>` / `IMessageHandler<TRequest, TResponse>` как **scoped**.
- Медиатор — **scoped**; на `Send` из скоупа резолвится только нужный хендлер и только его зависимости, а не все хендлеры разом.
- Попытка зарегистрировать два хендлера на одно сообщение — ошибка на старте, а не сюрприз в рантайме.

## Подключение

```csharp
builder.AddMediator();        // WebApplicationBuilder
// или
services.AddMediator();       // IServiceCollection
```

Опции `MediatorOption`:

- `AddHandlers = true` (по умолчанию) — автоскан реализаций `IMessageHandler` по сборкам и их регистрация;
- `Assemblies` — какие сборки сканировать; не задано — все загруженные в `AppDomain`.

Открытые generic-хендлеры (`class MyHandler<T> : IMessageHandler<T>`) скан не берёт — регистрируй руками:

```csharp
services.AddScoped(typeof(IMessageHandler<>), typeof(MyHandler<>));
```

## Использование

```csharp
// сообщение
public record Ping(string Text);

// хендлер сообщения
public class PingHandler : IMessageHandler<Ping>
{
	public Task HandleAsync(Ping message, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}

// запрос с ответом
public record Echo(string Text);

public class EchoHandler : IMessageHandler<Echo, string>
{
	public Task<string> HandleAsync(Echo request, CancellationToken cancellationToken = default)
	{
		return Task.FromResult($"echo: {request.Text}");
	}
}
```

Отправка:

```csharp
await mediator.SendAsync(new Ping("привет"));

string answer = await mediator.SendAsync<Echo, string>(new Echo("привет"));
```

Хендлер не зарегистрирован — `InvalidOperationException` от DI с точным именем интерфейса, который не найден.

## Изменения с 0.1

Хендлеры больше не регистрируются маркером `IMessageHandler`: инъекция `IEnumerable<IMessageHandler>` больше не собирает хендлеры, диспетч идёт только по закрытым интерфейсам.
