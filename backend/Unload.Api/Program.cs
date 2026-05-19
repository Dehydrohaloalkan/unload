using Unload.Api;
using Unload.Api.ErrorHandling;
using Unload.Api.Services;
using Unload.Bootstrapper.DependencyInjection;
using Unload.Tasks.MainUnload;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Host.UseNLog();

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ApiProblemDetailsFactory>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddScoped<OutputFilesService>();
builder.Services.AddUnloadRuntime(builder.Configuration);
builder.Services.AddHostedService<HistoryRetentionBackgroundService>();
builder.Services.AddHostedService<MainUnloadHostedService>();
builder.Services.AddHostedService<ProbeSchedulerHostedService>();
builder.Services.AddHostedService<SenderFeedbackProjectionBackgroundService>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();
app.MapHub<RunStatusHub>("/hubs/status");
app.Run();
