using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Unload.Api;
using Unload.Api.ErrorHandling;
using Unload.Api.Services;
using Unload.Bootstrapper.DependencyInjection;
using Unload.Tasks.MainUnload;
using Microsoft.AspNetCore.Mvc.Controllers;
using NLog.Web;

#if DESKTOP_BUILD
var desktopBuild = true;
#else
var desktopBuild = args.Contains("--desktop", StringComparer.OrdinalIgnoreCase);
#endif

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = desktopBuild ? AppContext.BaseDirectory : null,
});
if (desktopBuild)
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}
var openApiGenerationOnly = builder.Configuration.GetValue<bool>("OpenApiGenerationOnly");
builder.Logging.ClearProviders();
builder.Host.UseNLog();

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer((operation, context, _) =>
    {
        if (context.Description.ActionDescriptor is ControllerActionDescriptor action)
        {
            operation.OperationId = $"{action.ControllerName}_{action.ActionName}";
        }

        return Task.CompletedTask;
    });
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Servers = [];
        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ApiProblemDetailsFactory>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddScoped<OutputFilesService>();
builder.Services.AddUnloadRuntime(builder.Configuration, registerBackgroundServices: !openApiGenerationOnly);
if (!openApiGenerationOnly)
{
    builder.Services.AddSingleton<IRunStatusLiveTransport, SignalRRunStatusLiveTransport>();
    builder.Services.AddSingleton<RunStatusLivePublisher>();
    builder.Services.AddHostedService<HistoryRetentionBackgroundService>();
    builder.Services.AddHostedService<MainUnloadHostedService>();
    builder.Services.AddHostedService<ExtraUnloadHostedService>();
    builder.Services.AddHostedService<ProbeSchedulerHostedService>();
    builder.Services.AddHostedService<SenderFeedbackProjectionBackgroundService>();
}

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();
app.MapHub<RunStatusHub>(RunStatusHubContract.HubPath);
var webRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(webRoot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

if (!desktopBuild)
{
    app.Run();
    return;
}

await app.StartAsync();
var server = app.Services.GetRequiredService<IServer>();
var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault()
    ?? throw new InvalidOperationException("Desktop server address was not assigned.");
try
{
    var browserStart = OperatingSystem.IsWindows()
        ? new ProcessStartInfo(address) { UseShellExecute = true }
        : new ProcessStartInfo("xdg-open") { UseShellExecute = false };
    if (!OperatingSystem.IsWindows())
    {
        browserStart.ArgumentList.Add(address);
    }
    Process.Start(browserStart);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not open the desktop application in the default browser. URL: {Address}", address);
}

await app.WaitForShutdownAsync();

public partial class Program;
