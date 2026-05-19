using Microsoft.Extensions.Logging;
using Unload.Core;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Tasks.Preset;

/// <summary>
/// Задача probe: выполняет SQL из <see cref="PresetGateOptions.ProbeSql"/>
/// и применяет результат к <see cref="DailyWindowPolicy"/>.
/// Синхронная, не требует дневного окна, без зависимостей и конфликтов.
/// Завершение фиксируется в <see cref="TaskExecutionHistoryStore"/>,
/// чтобы <see cref="PresetTask.RequiresCompleted"/> мог быть проверен воркфлоу.
/// </summary>
public class ProbeTask(
    PresetGateOptions gateOptions,
    DailyWindowPolicy dailyWindowPolicy,
    IDatabaseClientFactory databaseClientFactory,
    TaskExecutionHistoryStore historyStore,
    ILogger<ProbeTask> logger) : UnloadTask
{
    private readonly PresetGateOptions _gateOptions = gateOptions;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly IDatabaseClientFactory _databaseClientFactory = databaseClientFactory;
    private readonly TaskExecutionHistoryStore _historyStore = historyStore;
    private readonly ILogger<ProbeTask> _logger = logger;

    public override string Code => TaskCodes.Probe;

    // RequiresCompleted, ConflictsWith, RequiresDailyWindowOpen — все false/empty по умолчанию.

    public override async Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var correlationId = BuildCorrelationId("probe");

        var probeResult = await ProbeAsync(cancellationToken);

        _dailyWindowPolicy.StartPolling();
        var changed = _dailyWindowPolicy.ApplyProbeResult(probeResult, now);

        if (changed)
        {
            _logger.LogInformation("Preset probe applied. ProbeResult: {ProbeResult}", probeResult);
        }

        var message = $"Probe completed. Result: {probeResult}.";
        _historyStore.Add(
            TaskCodes.Probe,
            now,
            now,
            correlationId,
            message,
            scriptsExecuted: null,
            filesWritten: null,
            outputPath: null);

        return new TaskExecutionResult(
            TaskCode: Code,
            ExecutionId: correlationId,
            Status: TaskExecutionStatus.Completed,
            Message: message);
    }

    private async Task<int> ProbeAsync(CancellationToken cancellationToken)
    {
        var client = _databaseClientFactory.CreateClient();
        try
        {
            await using var reader = await client.GetDataReaderAsync(_gateOptions.ProbeSql, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return 0;
            }

            if (reader.FieldCount == 0 || reader.IsDBNull(0))
            {
                return 0;
            }

            return Convert.ToInt32(reader.GetValue(0));
        }
        finally
        {
            await DisposeClientAsync(client);
        }
    }

    private static async Task DisposeClientAsync(IDatabaseClient client)
    {
        if (client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string BuildCorrelationId(string prefix)
    {
        return TaskCorrelationId.Create(prefix);
    }
}
