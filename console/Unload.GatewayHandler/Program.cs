using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Unload.GatewayHandler;

string watchDir = args.Length > 0 ? args[0] : "./ftp-root";

await Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton(new GatewayHandlerOptions { WatchDirectory = watchDir });
        services.AddHostedService<GatewayHandlerService>();
    })
    .UseConsoleLifetime()
    .Build()
    .RunAsync();
