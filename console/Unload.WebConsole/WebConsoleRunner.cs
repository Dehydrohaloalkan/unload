using Microsoft.AspNetCore.SignalR.Client;
using Spectre.Console;

namespace Unload.WebConsole;

/// <summary>
/// Точка оркестрации web-консоли: старт/подключение к запуску и live-мониторинг.
/// </summary>
internal static class WebConsoleRunner
{
    /// <summary>
    /// Сессионное состояние клиента web-консоли.
    /// </summary>
    private sealed class RunnerSessionState
    {
        /// <summary>
        /// CorrelationId запуска, за которым ведется наблюдение.
        /// </summary>
        public string TrackedCorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// Признак, что пользователь запросил остановку (Ctrl+C).
        /// </summary>
        public bool StopRequestedByUser { get; set; }

        /// <summary>
        /// Признак, что запрос на остановку уже отправлен в API.
        /// </summary>
        public bool StopRequestSent { get; set; }
    }

    /// <summary>
    /// Запускает приложение web-консоли и отслеживает жизненный цикл выгрузки.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    public static async Task RunAsync(string[] args)
    {
        var options = AppOptions.Parse(args);
        using var cts = new CancellationTokenSource();
        var sessionState = new RunnerSessionState();
        var runCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiState = new UiState();
        using var httpClient = new HttpClient { BaseAddress = new Uri(options.ApiBaseUrl) };
        var apiClient = new RunApiClient(httpClient);

        ConsoleCancelEventHandler onCancelKeyPress = (_, e) =>
        {
            e.Cancel = true;
            sessionState.StopRequestedByUser = true;
        };
        Console.CancelKeyPress += onCancelKeyPress;

        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{options.ApiBaseUrl.TrimEnd('/')}/hubs/status")
            .WithAutomaticReconnect()
            .Build();

        try
        {
            RenderHeader(options);
            RegisterConnectionHandlers(connection, sessionState, uiState, runCompleted);
            await ConnectToHubAsync(connection, cts.Token);
            var presetState = await apiClient.GetPresetStateAsync(cts.Token);
            if (presetState is not null)
            {
                uiState.SetPresetState(presetState);
            }

            if (options.RunPresetTask)
            {
                await RunPresetTaskAsync(apiClient, uiState, cts.Token);
                return;
            }

            if (options.RunExtraTask)
            {
                await RunExtraTaskAsync(apiClient, uiState, cts.Token);
                return;
            }

            await ResolveTrackedRunAsync(options, apiClient, sessionState, uiState, cts.Token);

            if (string.IsNullOrWhiteSpace(sessionState.TrackedCorrelationId))
            {
                var currentPreset = uiState.GetSnapshot().PresetState;
                if (currentPreset?.RequiresPresetExecution == true && !currentPreset.PresetCompleted)
                {
                    AnsiConsole.MarkupLine("[yellow]Preset is required before starting main or extra tasks.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]No active run found. Nothing to watch.[/]");
                }
                return;
            }

            await TrySubscribeToRunAsync(connection, sessionState.TrackedCorrelationId, cts.Token);
            AnsiConsole.MarkupLine($"[grey]Watching run:[/] {Markup.Escape(sessionState.TrackedCorrelationId)}");

            var initialStatus = await LoadInitialStatusAsync(
                apiClient,
                sessionState.TrackedCorrelationId,
                uiState,
                cts.Token);
            if (initialStatus is not null && IsTerminalStatus(initialStatus.Status))
            {
                AnsiConsole.Write(RunDashboardBuilder.BuildFinal(uiState.GetSnapshot(), sessionState.TrackedCorrelationId));
                AnsiConsole.MarkupLine($"[green]Run already finished with status:[/] {initialStatus.Status}");
                return;
            }

            await RenderLiveDashboardAsync(apiClient, sessionState, uiState, runCompleted, cts);
            await WaitForRunCompletionAsync(apiClient, sessionState.TrackedCorrelationId, runCompleted, cts.Token);
            await RefreshFinalStateAsync(apiClient, sessionState.TrackedCorrelationId, uiState, cts.Token);
            RenderFinalSummary(uiState.GetSnapshot(), sessionState.TrackedCorrelationId);
        }
        finally
        {
            Console.CancelKeyPress -= onCancelKeyPress;
        }
    }

    /// <summary>
    /// Печатает заголовок приложения и целевой API endpoint.
    /// </summary>
    /// <param name="options">Параметры запуска web-консоли.</param>
    private static void RenderHeader(AppOptions options)
    {
        AnsiConsole.Write(new Rule("[green]Unload WebConsole[/]").RuleStyle("green").LeftJustified());
        AnsiConsole.MarkupLine($"[grey]API:[/] {Markup.Escape(options.ApiBaseUrl)}");
    }

    /// <summary>
    /// Регистрирует обработчики входящих SignalR-сообщений.
    /// </summary>
    /// <param name="connection">Подключение к SignalR hub.</param>
    /// <param name="sessionState">Состояние текущей сессии клиента.</param>
    /// <param name="uiState">Потокобезопасное состояние UI.</param>
    /// <param name="runCompleted">Сигнал завершения запуска.</param>
    private static void RegisterConnectionHandlers(
        HubConnection connection,
        RunnerSessionState sessionState,
        UiState uiState,
        TaskCompletionSource<bool> runCompleted)
    {
        connection.On<RunnerEventDto>("status", @event =>
        {
            if (!ShouldProcessCorrelation(sessionState.TrackedCorrelationId, @event.CorrelationId))
            {
                return;
            }

            uiState.AddEvent(@event);
        });

        connection.On<RunStatusInfoDto>("run_status", status =>
        {
            if (!ShouldProcessCorrelation(sessionState.TrackedCorrelationId, status.CorrelationId))
            {
                return;
            }

            uiState.SetStatus(status);
            if (IsTerminalStatus(status.Status))
            {
                runCompleted.TrySetResult(true);
            }
        });

        connection.On<PresetGateStateDto>("preset_state", presetState =>
        {
            uiState.SetPresetState(presetState);
        });
    }

    /// <summary>
    /// Подключается к SignalR с визуальным индикатором ожидания.
    /// </summary>
    /// <param name="connection">Подключение к SignalR hub.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    private static async Task ConnectToHubAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Connecting to SignalR...", async _ => await connection.StartAsync(cancellationToken));
    }

    /// <summary>
    /// Определяет запуск для отслеживания: либо активный, либо новый, если активного нет.
    /// </summary>
    /// <param name="options">Параметры запуска web-консоли.</param>
    /// <param name="apiClient">HTTP-клиент API.</param>
    /// <param name="sessionState">Состояние текущей сессии клиента.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    private static async Task ResolveTrackedRunAsync(
        AppOptions options,
        RunApiClient apiClient,
        RunnerSessionState sessionState,
        UiState uiState,
        CancellationToken cancellationToken)
    {
        sessionState.TrackedCorrelationId = await apiClient.ResolveActiveCorrelationIdAsync(cancellationToken) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(sessionState.TrackedCorrelationId))
        {
            AnsiConsole.MarkupLine($"[yellow]Active run detected:[/] {Markup.Escape(sessionState.TrackedCorrelationId)}");
            AnsiConsole.MarkupLine("[grey]Starting a new run is disabled until the active run is finished.[/]");
            return;
        }

        var selectedMemberCodes = options.MemberCodes.Count > 0
            ? options.MemberCodes
            : await PromptMemberCodesAsync(apiClient, cancellationToken);
        if (selectedMemberCodes.Count == 0)
        {
            return;
        }

        var presetState = uiState.GetSnapshot().PresetState;
        if (presetState?.RequiresPresetExecution == true && !presetState.PresetCompleted)
        {
            AnsiConsole.MarkupLine("[yellow]Main run is locked until preset task is completed.[/]");
            return;
        }

        var startResult = await apiClient.StartRunAsync(selectedMemberCodes, cancellationToken);
        if (startResult.Accepted is not null)
        {
            sessionState.TrackedCorrelationId = startResult.Accepted.CorrelationId;
            AnsiConsole.MarkupLine($"[green]Run started:[/] {Markup.Escape(sessionState.TrackedCorrelationId)}");
            return;
        }

        sessionState.TrackedCorrelationId = startResult.Conflict?.ActiveCorrelationId ?? string.Empty;
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(startResult.Conflict?.Message ?? "Run is already in progress.")}[/]");
    }

    private static async Task RunPresetTaskAsync(
        RunApiClient apiClient,
        UiState uiState,
        CancellationToken cancellationToken)
    {
        var presetState = uiState.GetSnapshot().PresetState;
        if (presetState is not null && (!presetState.ReadyForPreset || presetState.PresetCompleted))
        {
            AnsiConsole.MarkupLine($"[yellow]Preset is not available:[/] {Markup.Escape(presetState.Message)}");
            return;
        }

        var result = await apiClient.RunPresetAsync(cancellationToken);
        AnsiConsole.MarkupLine(
            $"[green]Preset completed.[/] Scripts: {result.ScriptsExecuted}, CorrelationId: {Markup.Escape(result.CorrelationId)}");
    }

    private static async Task RunExtraTaskAsync(
        RunApiClient apiClient,
        UiState uiState,
        CancellationToken cancellationToken)
    {
        var presetState = uiState.GetSnapshot().PresetState;
        if (presetState?.RequiresPresetExecution == true && !presetState.PresetCompleted)
        {
            AnsiConsole.MarkupLine("[yellow]Extra task is locked until preset task is completed.[/]");
            return;
        }

        var result = await apiClient.RunExtraAsync(cancellationToken);
        AnsiConsole.MarkupLine(
            $"[green]Extra completed.[/] Scripts: {result.ScriptsExecuted}, Files: {result.FilesWritten}, Output: {Markup.Escape(result.OutputPath ?? "-")}");
    }

    /// <summary>
    /// Пытается подписаться на конкретный запуск в SignalR hub.
    /// </summary>
    /// <param name="connection">Подключение к SignalR hub.</param>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    private static async Task TrySubscribeToRunAsync(
        HubConnection connection,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.InvokeAsync("SubscribeRun", correlationId, cancellationToken);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Загружает исходный статус запуска и синхронизирует его в UI state.
    /// </summary>
    /// <param name="apiClient">HTTP-клиент API.</param>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="uiState">Потокобезопасное состояние UI.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Загруженный статус или <c>null</c>, если запуск не найден.</returns>
    private static async Task<RunStatusInfoDto?> LoadInitialStatusAsync(
        RunApiClient apiClient,
        string correlationId,
        UiState uiState,
        CancellationToken cancellationToken)
    {
        var initialStatus = await apiClient.GetRunStatusAsync(correlationId, cancellationToken);
        if (initialStatus is not null)
        {
            uiState.SetStatus(initialStatus);
        }

        return initialStatus;
    }

    /// <summary>
    /// Рендерит live-дашборд до завершения запуска или отмены.
    /// </summary>
    /// <param name="apiClient">HTTP-клиент API.</param>
    /// <param name="sessionState">Состояние текущей сессии клиента.</param>
    /// <param name="uiState">Потокобезопасное состояние UI.</param>
    /// <param name="runCompleted">Сигнал завершения запуска.</param>
    /// <param name="cancellationTokenSource">Источник токена отмены.</param>
    private static async Task RenderLiveDashboardAsync(
        RunApiClient apiClient,
        RunnerSessionState sessionState,
        UiState uiState,
        TaskCompletionSource<bool> runCompleted,
        CancellationTokenSource cancellationTokenSource)
    {
        await AnsiConsole.Live(RunDashboardBuilder.Build(uiState.GetSnapshot(), sessionState.TrackedCorrelationId))
            .AutoClear(true)
            .StartAsync(async context =>
            {
                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    await TrySendStopRequestAsync(apiClient, sessionState, cancellationTokenSource);

                    context.UpdateTarget(RunDashboardBuilder.Build(uiState.GetSnapshot(), sessionState.TrackedCorrelationId));
                    context.Refresh();

                    var done = await Task.WhenAny(runCompleted.Task, Task.Delay(300, cancellationTokenSource.Token));
                    if (done == runCompleted.Task)
                    {
                        break;
                    }
                }
            });
    }

    /// <summary>
    /// При необходимости отправляет в API запрос на остановку активного запуска.
    /// </summary>
    /// <param name="apiClient">HTTP-клиент API.</param>
    /// <param name="sessionState">Состояние текущей сессии клиента.</param>
    /// <param name="cancellationTokenSource">Источник токена отмены.</param>
    private static async Task TrySendStopRequestAsync(
        RunApiClient apiClient,
        RunnerSessionState sessionState,
        CancellationTokenSource cancellationTokenSource)
    {
        if (!sessionState.StopRequestedByUser ||
            sessionState.StopRequestSent ||
            string.IsNullOrWhiteSpace(sessionState.TrackedCorrelationId))
        {
            return;
        }

        sessionState.StopRequestSent = true;
        var accepted = await apiClient.StopRunAsync(sessionState.TrackedCorrelationId, cancellationTokenSource.Token);
        if (accepted)
        {
            AnsiConsole.MarkupLine("[yellow]Stop requested.[/]");
            return;
        }

        AnsiConsole.MarkupLine("[red]Stop request was rejected by API.[/]");
        cancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Ждет завершения запуска, периодически проверяя статус через API.
    /// </summary>
    /// <param name="apiClient">Клиент для запросов статуса.</param>
    /// <param name="correlationId">Идентификатор отслеживаемого запуска.</param>
    /// <param name="runCompleted">Сигнал о завершении из SignalR.</param>
    /// <param name="cancellationToken">Токен отмены ожидания.</param>
    private static async Task WaitForRunCompletionAsync(
        RunApiClient apiClient,
        string correlationId,
        TaskCompletionSource<bool> runCompleted,
        CancellationToken cancellationToken)
    {
        using var pollTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!cancellationToken.IsCancellationRequested)
        {
            if (runCompleted.Task.IsCompleted)
            {
                return;
            }

            if (!await pollTimer.WaitForNextTickAsync(cancellationToken))
            {
                return;
            }

            if (runCompleted.Task.IsCompleted)
            {
                return;
            }

            var state = await apiClient.GetRunStatusAsync(correlationId, cancellationToken);
            if (state is null)
            {
                continue;
            }

            if (state.Status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed or RunLifecycleStatus.Cancelled)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Обновляет локальный UI state финальным статусом запуска из API.
    /// </summary>
    /// <param name="apiClient">HTTP-клиент API.</param>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="uiState">Потокобезопасное состояние UI.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    private static async Task RefreshFinalStateAsync(
        RunApiClient apiClient,
        string correlationId,
        UiState uiState,
        CancellationToken cancellationToken)
    {
        var finalState = await apiClient.GetRunStatusAsync(correlationId, cancellationToken);
        if (finalState is not null)
        {
            uiState.SetStatus(finalState);
        }
    }

    /// <summary>
    /// Выводит финальный snapshot запуска и итоговую строку статуса.
    /// </summary>
    /// <param name="finalSnapshot">Финальный снимок состояния запуска.</param>
    /// <param name="correlationId">Идентификатор запуска.</param>
    private static void RenderFinalSummary(UiSnapshot finalSnapshot, string correlationId)
    {
        AnsiConsole.Write(RunDashboardBuilder.BuildFinal(finalSnapshot, correlationId));
        var finalColor = finalSnapshot.Status switch
        {
            RunLifecycleStatus.Completed => "green",
            RunLifecycleStatus.Failed => "red",
            RunLifecycleStatus.Cancelled => "yellow",
            _ => "yellow"
        };
        AnsiConsole.MarkupLine(
            $"[{finalColor}]Run finished:[/] {finalSnapshot.Status} {Markup.Escape(finalSnapshot.Message ?? string.Empty)}");
    }

    /// <summary>
    /// Проверяет, что входящее событие относится к отслеживаемому запуску.
    /// </summary>
    /// <param name="trackedCorrelationId">Текущий отслеживаемый correlationId.</param>
    /// <param name="incomingCorrelationId">CorrelationId входящего события/статуса.</param>
    /// <returns><c>true</c>, если событие нужно обработать.</returns>
    private static bool ShouldProcessCorrelation(string trackedCorrelationId, string incomingCorrelationId)
    {
        return string.IsNullOrWhiteSpace(trackedCorrelationId) ||
               string.Equals(incomingCorrelationId, trackedCorrelationId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Возвращает признак terminal-статуса запуска.
    /// </summary>
    /// <param name="status">Статус запуска.</param>
    /// <returns><c>true</c>, если запуск завершен.</returns>
    private static bool IsTerminalStatus(RunLifecycleStatus status)
    {
        return status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed or RunLifecycleStatus.Cancelled;
    }

    /// <summary>
    /// Загружает список мемберов и запрашивает выбор у пользователя.
    /// </summary>
    /// <param name="apiClient">HTTP-клиент API.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Список выбранных кодов мемберов.</returns>
    private static async Task<IReadOnlyCollection<string>> PromptMemberCodesAsync(
        RunApiClient apiClient,
        CancellationToken cancellationToken)
    {
        var members = await apiClient.GetMembersAsync(cancellationToken);
        if (members.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No members available in API catalog. Switching to watch mode.[/]");
            return Array.Empty<string>();
        }

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<MemberCatalogItemDto>()
                .Title("Select [green]members[/] to run ([grey]Enter to watch active run[/])")
                .NotRequired()
                .PageSize(12)
                .InstructionsText("[grey](Use [blue]<space>[/] to toggle, [blue]<enter>[/] to accept)[/]")
                .UseConverter(static member =>
                {
                    var statusText = member.ActiveRunStatus is null
                        ? "idle"
                        : member.ActiveRunStatus.Status.ToString();
                    return $"{member.Code} - {member.Name} (targets: {member.TargetCodes.Count}, status: {statusText})";
                })
                .AddChoices(members.OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)));

        return selected
            .Select(static x => x.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
