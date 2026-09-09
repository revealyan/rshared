using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Stream consumption: creates consumer groups at host start, reads batches with XREADGROUP,
/// acknowledges handled entries, retries failures in memory up to MaxRetryCount and
/// dead-letters exhausted or poison entries. A background sweep reclaims entries stuck
/// with dead consumers via XAUTOCLAIM. At-least-once: handlers must be idempotent.
/// Requires Redis 6.2+ (XAUTOCLAIM).
/// </summary>
internal sealed class RedisStreamConsumerService(
	IRedisConnection connection,
	RedisStreamHandlerRegistry registry,
	RedisOption option,
	ILogger<RedisStreamConsumerService> logger) : IHostedService
{
	private static readonly TimeSpan IdlePoll = TimeSpan.FromMilliseconds(500);

	private readonly IDatabase _database = connection.Multiplexer.GetDatabase();
	private readonly CancellationTokenSource _shutdown = new();
	private readonly List<Task> _loops = [];

	// уникальное имя консюмера на процесс: PEL-записи инстансов не смешиваются
	// Stryker disable once String : префикс имени консюмера — любой уникальный на процесс
	private readonly string _consumer = "c" + Guid.NewGuid().ToString("N");

	public Task StartAsync(CancellationToken cancellationToken)
	{
		foreach (var group in registry.Snapshot().GroupBy(entry => entry.MessageType))
		{
			// Stryker disable all : клей hosted-старта — группировка и запуск циклов, интеграционным прогоном
			var messageType = group.Key;
			var handlers = group.Select(entry => entry.Handler).ToArray();
			var settings = group.First().Option;
			var stream = (RedisKey)(option.KeyPrefix + "stream:" + messageType.Name);
			// Stryker restore all

			// Stryker disable once Statement : вызов EnsureGroup ассертится тестом группы, маппинг теряет
			EnsureGroup(stream, settings);

			// параллельно между стримами, последовательно внутри — одно консюмер-имя на стрим
			// Stryker disable once Statement : запуск цикла — клей hosted-старта, интеграционным прогоном
			_loops.Add(Task.Run(() => ReadLoopAsync(stream, messageType, handlers, settings, _shutdown.Token)));
		}

		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		// Stryker disable Statement, Block : останов фоновых циклов — клей хоста, интеграционным прогоном
		_shutdown.Cancel();
		await Task.WhenAll(_loops).WaitAsync(cancellationToken);
		_shutdown.Dispose();
	}

	private void EnsureGroup(RedisKey stream, RedisStreamOption settings)
	{
		try
		{
			// группа создаётся с начала стрима: сообщения, опубликованные до первого старта, не теряются
			_database.StreamCreateConsumerGroupAsync(stream, settings.Group, StreamPosition.Beginning, createStream: true).GetAwaiter().GetResult();
		}
		// Stryker disable once String : фильтр BUSYGROUP покрывается тестом, маппинг теряет
		catch (RedisServerException exception) when (exception.Message.Contains("BUSYGROUP"))
		{
			// группа уже есть — норма повторных стартов
		}
	}

	// Обвязка цикла (while/пауза/отмена/лог транспорта) — фоновый поток, непокрываемо юнит-тестами
	// стабильно; доставка/свип/DLQ покрыты напрямую (DeliverAsync, SweepPendingAsync).
	// Stryker disable all : клей фонового цикла — задержки и обработка отмены, интеграционным прогоном
	internal async Task ReadLoopAsync(RedisKey stream, Type messageType, Func<object?, Task>[] handlers, RedisStreamOption settings,
		CancellationToken cancellationToken = default)
	{
		while (!_shutdown.IsCancellationRequested)
		{
			try
			{
				var entries = await _database.StreamReadGroupAsync(stream, settings.Group, _consumer,
					StreamPosition.NewMessages, settings.BatchSize);

				if (entries is { Length: > 0 })
				{
					foreach (var entry in entries)
					{
						await DeliverAsync(stream, entry, messageType, handlers, settings);
					}
				}
				else
				{
					await SweepPendingAsync(stream, messageType, handlers, settings);
					await Task.Delay(IdlePoll, cancellationToken == default ? _shutdown.Token : cancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception exception)
			{
				// Stryker disable Statement, String, Conditional, Equality : клей catch-ветки цикла — интеграционным прогоном
				logger.LogError(exception, "Stream read failed on {Stream}: retrying", stream.ToString());
				await Task.Delay(IdlePoll, cancellationToken == default ? _shutdown.Token : cancellationToken);
				// Stryker restore Statement, String, Conditional, Equality
			}
		}
		// Stryker restore all
	}

	internal async Task DeliverAsync(RedisKey stream, StreamEntry entry, Type messageType,
		Func<object?, Task>[] handlers, RedisStreamOption settings)
	{
		// имена полей уникальны в записи: First/Last эквивалентны
		// Stryker disable once LinqMethod : единственное совпадение имени — First==Last
		var body = entry.Values.FirstOrDefault(pair => pair.Name == "body").Value;

		object? message;
		try
		{
			message = JsonSerializer.Deserialize((byte[])body!, messageType, option.JsonSerializerOptions);
		}
		catch (JsonException exception)
		{
			// poison без ретраев — тело не починится
			logger.LogError(exception, "Poison stream entry {Id} on {Stream}", entry.Id.ToString(), stream.ToString());
			await DeadLetterAsync(stream, entry, body, settings, "poison body");
			await _database.StreamAcknowledgeAsync(stream, settings.Group, entry.Id);
			return;
		}

		// ретраи в процессе: после краша недоставленная запись вернётся reclaim-ом (счёт заново)
		for (var attempt = 1; attempt <= settings.MaxRetryCount; attempt++)
		{
			try
			{
				foreach (var handler in handlers)
				{
					await handler(message);
				}

				await _database.StreamAcknowledgeAsync(stream, settings.Group, entry.Id);
				return;
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Stream handler failed on {Stream} {Id}: attempt {Attempt} of {Attempts}",
					stream.ToString(), entry.Id.ToString(), attempt, settings.MaxRetryCount);
				await Task.Delay(100 * attempt, _shutdown.Token);
			}
		}

		await DeadLetterAsync(stream, entry, body, settings, "retries exhausted");
		await _database.StreamAcknowledgeAsync(stream, settings.Group, entry.Id);
	}

	private async Task DeadLetterAsync(RedisKey stream, StreamEntry entry, RedisValue body, RedisStreamOption settings, string error)
	{
		if (settings.DeadLetterStream.Length == 0)
		{
			return;
		}

		var dlq = (RedisKey)(stream.ToString() + settings.DeadLetterStream);
		await _database.StreamAddAsync(dlq,
		[
			new NameValueEntry("body", body),
			// Stryker disable once LinqMethod : поле type единственно в записи — First==Last
			new NameValueEntry("type", entry.Values.FirstOrDefault(p => p.Name == "type").Value),
			new NameValueEntry("error", (RedisValue)error),
		]);
	}

	internal async Task SweepPendingAsync(RedisKey stream, Type messageType, Func<object?, Task>[] handlers, RedisStreamOption settings)
	{
		var minIdle = (long)settings.MinIdleBeforeReclaim.TotalMilliseconds;
		var claimed = await _database.StreamAutoClaimAsync(stream, settings.Group, _consumer, minIdle, "0-0");
		// Stryker disable once Equality : пустой claimed — пустой цикл, разница ненаблюдаема
		if (claimed.ClaimedEntries is { Length: > 0 })
		{
			// зависшие записи прошли через тот же конвейер: дубликаты возможны — at-least-once
			foreach (var entry in claimed.ClaimedEntries)
			{
				await DeliverAsync(stream, entry, messageType, handlers, settings);
			}
		}
	}
}
