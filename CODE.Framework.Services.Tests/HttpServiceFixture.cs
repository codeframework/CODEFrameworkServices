using System.Net;
using CODE.Framework.Services.Client;
using CODE.Framework.Services.Server.AspNetCore;
using CODE.Framework.Services.Server.AspNetCore.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

// Framework configuration and proxy caches are process-wide. Keep tests serial;
// concurrency is exercised explicitly within a single test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CODE.Framework.Services.Tests;

public class HttpServiceFixture : IAsyncLifetime
{
    private WebApplication _app;
    private ServiceHandlerConfiguration _previousConfiguration;
    public ExecutionLog Log { get; } = new();
    public AsyncServiceState AsyncState { get; } = new();
    public string ServiceUrl { get; private set; }
    public Func<HttpContext, Task<bool>> InterceptRequest { get; set; }
    public ServiceHandlerConfigurationInstance TestConfiguration { get; private set; }
    public ServiceHandlerConfigurationInstance AsyncConfiguration { get; private set; }

    public async Task InitializeAsync()
    {
        _previousConfiguration = ServiceHandlerConfiguration.Current;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(Log);
        builder.Services.AddSingleton(AsyncState);
        builder.Services.AddHostedServices(config =>
        {
            config.Services.Clear();
            config.Cors.AllowedHeaders = "Content-Type,X-Api-Key,Customer-Key,Async-Key";
            TestConfiguration = new ServiceHandlerConfigurationInstance { ServiceType = typeof(TestService), RouteBasePath = "/test" };
            AsyncConfiguration = new ServiceHandlerConfigurationInstance { ServiceType = typeof(AsyncTestService), RouteBasePath = "/async" };
            config.Services.Add(TestConfiguration);
            config.Services.Add(AsyncConfiguration);
        });
        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            var interceptor = InterceptRequest;
            if (interceptor == null || !await interceptor(context)) await next(context);
        });
        _app.UseServiceHandler();
        _app.UseOpenApiHandler();
        _app.UseMcpHandler();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _app.StartAsync(timeout.Token);
        var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        ServiceUrl = Assert.Single(addresses.Addresses) + "/test";
        ServiceClient.ClearSettingsCache();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_app != null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await _app.StopAsync(timeout.Token);
                await _app.DisposeAsync();
            }
        }
        finally
        {
            ServiceClient.ClearSettingsCache();
            ServiceHandlerConfiguration.Current = _previousConfiguration;
        }
    }
}
