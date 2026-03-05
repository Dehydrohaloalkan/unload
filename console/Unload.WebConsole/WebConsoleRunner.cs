using Microsoft.AspNetCore.SignalR.Client;
using Spectre.Console;

namespace Unload.WebConsole;

internal static class WebConsoleRunner
{
    public static async Task RunAsync(string[] args)
    {
        var options = AppOptions.Parse(args);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var trackedCorrelationId = string.Empty;
        var runCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiState = new UiState();
        using var httpClient = new HttpClient { BaseAddress = new Uri(options.ApiBaseUrl) };
        var apiClient = new RunApiClient(httpClient);
        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{options.ApiBaseUrl.TrimEnd('/')}/hubs/status")
            .WithAutomaticReconnect()
            .Build();

        AnsiConsole.Write(new Rule("[green]Unload WebConsole[/]").RuleStyle("green").LeftJustified());
        AnsiConsole.MarkupLine($"[grey]API:[/] {Markup.Escape(options.ApiBaseUrl)}");

        connection.On<RunnerEventDto>("status", @event =>
        {
            if (!string.IsNullOrWhiteSpace(trackedCorrelationId) &&
                !string.Equals(@event.CorrelationId, trackedCorrelationId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            uiState.AddEvent(@event);
        });

        connection.On<RunStatusInfoDto>("run_status", status =>
        {
            if (!string.IsNullOrWhiteSpace(trackedCorrelationId) &&
                !string.Equals(status.CorrelationId, trackedCorrelationId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            uiState.SetStatus(status);
            if (status.Status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed)
            {
                runCompleted.TrySetResult(true);
            }
        });

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Connecting to SignalR...", async _ => await connection.StartAsync(cts.Token));

        if (options.TargetCodes.Count > 0)
        {
            var startResult = await apiClient.StartRunAsync(options.TargetCodes, cts.Token);
            if (startResult.Accepted is not null)
            {
                trackedCorrelationId = startResult.Accepted.CorrelationId;
                AnsiConsole.MarkupLine($"[green]Run started:[/] {Markup.Escape(trackedCorrelationId)}");
            }
            else
            {
                trackedCorrelationId = startResult.Conflict?.ActiveCorrelationId ?? string.Empty;
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(startResult.Conflict?.Message ?? "Run is already in progress.")}[/]");
            }
        }

        if (string.IsNullOrWhiteSpace(trackedCorrelationId))
        {
            trackedCorrelationId = await apiClient.ResolveActiveCorrelationIdAsync(cts.Token) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(trackedCorrelationId))
        {
            AnsiConsole.MarkupLine("[yellow]No active run found. Nothing to watch.[/]");
            return;
        }

        try
        {
            await connection.InvokeAsync("SubscribeRun", trackedCorrelationId, cts.Token);
        }
        catch
        {
        }

        AnsiConsole.MarkupLine($"[grey]Watching run:[/] {Markup.Escape(trackedCorrelationId)}");
        var initialStatus = await apiClient.GetRunStatusAsync(trackedCorrelationId, cts.Token);
        if (initialStatus is not null)
        {
            uiState.SetStatus(initialStatus);
        }

        if (initialStatus is not null &&
            initialStatus.Status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed)
        {
            AnsiConsole.MarkupLine($"[green]Run already finished with status:[/] {initialStatus.Status}");
            return;
        }

        await AnsiConsole.Live(RunDashboardBuilder.Build(uiState.GetSnapshot(), trackedCorrelationId))
            .AutoClear(false)
            .StartAsync(async context =>
            {
                while (!cts.IsCancellationRequested)
                {
                    context.UpdateTarget(RunDashboardBuilder.Build(uiState.GetSnapshot(), trackedCorrelationId));
                    context.Refresh();

                    var done = await Task.WhenAny(runCompleted.Task, Task.Delay(300, cts.Token));
                    if (done == runCompleted.Task)
                    {
                        break;
                    }
                }
            });

        await WaitForRunCompletionAsync(apiClient, trackedCorrelationId, runCompleted, cts.Token);
        var finalState = await apiClient.GetRunStatusAsync(trackedCorrelationId, cts.Token);
        if (finalState is not null)
        {
            uiState.SetStatus(finalState);
        }

        var finalSnapshot = uiState.GetSnapshot();
        var finalColor = finalSnapshot.Status switch
        {
            RunLifecycleStatus.Completed => "green",
            RunLifecycleStatus.Failed => "red",
            _ => "yellow"
        };
        AnsiConsole.MarkupLine(
            $"[{finalColor}]Run finished:[/] {finalSnapshot.Status} {Markup.Escape(finalSnapshot.Message ?? string.Empty)}");
    }

    private static async Task WaitForRunCompletionAsync(
        RunApiClient apiClient,
        string correlationId,
        TaskCompletionSource<bool> runCompleted,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var completedTask = await Task.WhenAny(runCompleted.Task, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
            if (completedTask == runCompleted.Task)
            {
                return;
            }

            var state = await apiClient.GetRunStatusAsync(correlationId, cancellationToken);
            if (state is null)
            {
                continue;
            }

            if (state.Status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed)
            {
                return;
            }
        }
    }
}
