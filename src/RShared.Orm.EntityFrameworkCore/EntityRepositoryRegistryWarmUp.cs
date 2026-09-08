using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RShared.Orm.EntityFrameworkCore;

/// <summary>
/// Прогрев реестра при старте хоста: карта владения и конфликты владельцев
/// всплывают на старте приложения, а не в первом реквесте
/// </summary>
internal sealed class EntityRepositoryRegistryWarmUp
	: IHostedService
{
	private readonly EntityRepositoryRegistry _registry;
	private readonly IServiceProvider _provider;

	public EntityRepositoryRegistryWarmUp(EntityRepositoryRegistry registry, IServiceProvider provider)
	{
		_registry = registry;
		_provider = provider;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		using var scope = _provider.CreateScope();

		_registry.WarmUp(scope.ServiceProvider);

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
