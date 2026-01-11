using Microsoft.Extensions.DependencyInjection;

namespace RShared.DependencyInjections;

public interface IRegistry
{
	public void RegisterServices(IServiceCollection services);
}
