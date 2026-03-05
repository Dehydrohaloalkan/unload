using Microsoft.AspNetCore.SignalR;
using Unload.Api;
using Unload.Application;
using Unload.Core;

var builder = WebApplication.CreateBuilder(args);

var root = ApiWorkspacePathResolver.ResolveWorkspaceRoot();
var scriptsDirectory = Path.Combine(root, "scripts");
var catalogPath = Path.Combine(root, "configs", "catalog.json");
var outputDirectory = Path.Combine(root, "output");

builder.Services.AddSignalR();
builder.Services.AddUnloadRuntime(new UnloadRuntimePaths(
    CatalogPath: catalogPath,
    ScriptsDirectory: scriptsDirectory,
    OutputDirectory: outputDirectory));
builder.Services.AddHostedService<RunProcessingBackgroundService>();

var app = builder.Build();

app.MapGet("/api/catalog", async (ICatalogService catalogService, CancellationToken cancellationToken) =>
{
    var catalog = await catalogService.GetCatalogAsync(cancellationToken);
    return Results.Ok(catalog);
});

app.MapGet("/api/members", async (
    ICatalogService catalogService,
    IRunCoordinator runCoordinator,
    IRunStateStore runStateStore,
    CancellationToken cancellationToken) =>
{
    var catalog = await catalogService.GetCatalogAsync(cancellationToken);
    var activeCorrelationId = runCoordinator.GetActiveCorrelationId();
    var activeRun = string.IsNullOrWhiteSpace(activeCorrelationId)
        ? null
        : runStateStore.Get(activeCorrelationId);

    var members = catalog.Members
        .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
        .Select(member =>
        {
            var targetCodes = catalog.Targets
                .Where(target => target.MemberId == member.Id)
                .Select(static target => target.TargetCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            MemberRunStatusInfo? activeStatus = null;
            if (activeRun?.MemberStatuses is not null &&
                activeRun.MemberStatuses.TryGetValue(member.Name, out var memberStatus))
            {
                activeStatus = memberStatus;
            }

            return new MemberCatalogItem(
                member.Code,
                member.Name,
                targetCodes,
                activeRun?.CorrelationId,
                activeStatus);
        })
        .ToArray();
    return Results.Ok(members);
});

app.MapPost("/api/runs", async (
    RunStartRequest request,
    ICatalogService catalogService,
    IRunOrchestrator orchestrator,
    IRunStateStore runStateStore,
    IHubContext<RunStatusHub> hubContext,
    CancellationToken cancellationToken) =>
{
    var normalizedMemberCodes = request.MemberCodes
        .Where(static x => !string.IsNullOrWhiteSpace(x))
        .Select(static x => x.Trim().ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (normalizedMemberCodes.Length == 0)
    {
        return Results.BadRequest(new { message = "At least one member code is required." });
    }

    var catalog = await catalogService.GetCatalogAsync(cancellationToken);
    var selectedMembers = catalog.Members
        .Where(member => normalizedMemberCodes.Contains(member.Code, StringComparer.OrdinalIgnoreCase))
        .ToArray();
    var selectedCodes = selectedMembers.Select(static x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var unknownCodes = normalizedMemberCodes.Where(code => !selectedCodes.Contains(code)).ToArray();
    if (unknownCodes.Length > 0)
    {
        return Results.BadRequest(new
        {
            message = $"Unknown member codes: {string.Join(", ", unknownCodes)}",
            unknownMemberCodes = unknownCodes
        });
    }

    var selectedMemberIds = selectedMembers.Select(static x => x.Id).ToHashSet();
    var targetCodes = catalog.Targets
        .Where(target => selectedMemberIds.Contains(target.MemberId))
        .Select(static target => target.TargetCode)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (targetCodes.Length == 0)
    {
        return Results.BadRequest(new { message = "No target codes found for selected members." });
    }

    string correlationId;
    try
    {
        correlationId = orchestrator.StartRun(targetCodes, selectedMembers.Select(static x => x.Name).ToArray());
    }
    catch (RunAlreadyInProgressException ex)
    {
        return Results.Conflict(new
        {
            message = ex.Message,
            activeCorrelationId = ex.ActiveCorrelationId
        });
    }

    var runState = runStateStore.Get(correlationId);
    if (runState is not null)
    {
        await hubContext.Clients.All.SendAsync("run_status", runState, cancellationToken);
    }

    return Results.Accepted(
        $"/api/runs/{correlationId}",
        new RunAcceptedResponse(
            correlationId,
            $"/api/runs/{correlationId}",
            "/hubs/status",
            "SubscribeRun",
            "status",
            "run_status",
            $"/api/runs/{correlationId}/stop"));
});

app.MapGet("/api/runs", (IRunStateStore runStateStore) => Results.Ok(runStateStore.List()));

app.MapGet("/api/runs/active", (IRunCoordinator runCoordinator, IRunStateStore runStateStore) =>
{
    var correlationId = runCoordinator.GetActiveCorrelationId();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        return Results.NotFound();
    }

    var run = runStateStore.Get(correlationId);
    return run is null
        ? Results.Ok(new { correlationId })
        : Results.Ok(run);
});

app.MapGet("/api/runs/{correlationId}", (string correlationId, IRunStateStore runStateStore) =>
{
    var run = runStateStore.Get(correlationId);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

app.MapPost("/api/runs/{correlationId}/stop", async (
    string correlationId,
    IRunCoordinator runCoordinator,
    IRunStateStore runStateStore,
    IHubContext<RunStatusHub> hubContext,
    CancellationToken cancellationToken) =>
{
    if (!runCoordinator.TryCancel(correlationId))
    {
        return Results.NotFound(new { message = "Active run with specified correlationId was not found." });
    }

    runStateStore.SetCancelled(correlationId, "Run cancellation requested.");
    var state = runStateStore.Get(correlationId);
    if (state is not null)
    {
        await hubContext.Clients.All.SendAsync("run_status", state, cancellationToken);
    }

    return Results.Accepted($"/api/runs/{correlationId}", new { correlationId, status = "cancellation_requested" });
});

app.MapHub<RunStatusHub>("/hubs/status");
app.Run();
