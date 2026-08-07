using Unload.Api;
using Unload.Api.ErrorHandling;
using Unload.Api.Services;
using Unload.Bootstrapper.DependencyInjection;
using Unload.Tasks.MainUnload;
using Microsoft.AspNetCore.Mvc.Controllers;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);
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
app.Run();

public partial class Program;
