using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using RShared.AuthKit;
using RShared.IdentityKit;
using RShared.Orm.EntityFrameworkCore;

namespace RShared.IdentityKit.Tests;

/// <summary>
/// Провайдер на SQLite in-memory c AuthKit + IdentityKit.
/// IAuthenticationService подменён заглушкой — SignIn уходит в неё, а не в cookie-хендлер
/// </summary>
internal sealed class IdentityKitFixture : IDisposable
{
	private readonly List<SqliteConnection> _connections = [];

	public ServiceProvider Provider { get; private set; } = null!;

	public IAuthenticationService Auth { get; private set; } = null!;

	public IHttpContextAccessor Accessor { get; private set; } = null!;

	public static IdentityKitFixture Create(Action<IdentityKitOption>? configure = null, bool withEmailSender = true)
	{
		var fixture = new IdentityKitFixture();

		var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		fixture._connections.Add(connection);

		var auth = Substitute.For<IAuthenticationService>();
		var accessor = Substitute.For<IHttpContextAccessor>();

		var services = new ServiceCollection()
			.AddEntityRepositories(typeof(KitContext));
		services.AddDbContext<KitContext>(options => options.UseSqlite(connection));

		// AuthKitService берёт контекст из accessor: регистрируем свой ДО AddAuthKit, TryAdd не перебьёт
		services.AddSingleton(accessor);
		services.AddAuthKit(_ => { });
		// внешний вход и SignIn идут через IAuthenticationService: подменяем на заглушку
		services.Replace(ServiceDescriptor.Singleton(auth));

		services.AddIdentityKit<IdentityKitUser>(o =>
		{
			o.CodeHashPepper = "pepper";
			configure?.Invoke(o);
		});
		if (withEmailSender)
		{
			services.AddScoped<IEmailSender, FakeEmailSender>();
		}

		fixture.Provider = services.BuildServiceProvider();
		fixture.Auth = auth;
		fixture.Accessor = accessor;

		using (var scope = fixture.Provider.CreateScope())
		{
			scope.ServiceProvider.GetRequiredService<KitContext>().Database.EnsureCreated();
		}

		return fixture;
	}

	/// <summary>
	/// Скоуп с настроенным HttpContext: SignIn из AuthKit уйдёт в заглушку IAuthenticationService
	/// </summary>
	public ScopeHandle OpenScope()
	{
		var scope = Provider.CreateScope();
		Accessor.HttpContext.Returns(new DefaultHttpContext { RequestServices = scope.ServiceProvider });
		return new ScopeHandle(scope, this);
	}

	public void Dispose()
	{
		Provider.Dispose();
		foreach (var connection in _connections)
		{
			connection.Dispose();
		}
	}

	internal sealed class ScopeHandle(IServiceScope scope, IdentityKitFixture fixture) : IDisposable
	{
		public IServiceProvider Services => scope.ServiceProvider;

		public T Get<T>()
			where T : notnull
		{
			return scope.ServiceProvider.GetRequiredService<T>();
		}

		public void Dispose()
		{
			fixture.Accessor.HttpContext.Returns((HttpContext?)null);
			scope.Dispose();
		}
	}
}
