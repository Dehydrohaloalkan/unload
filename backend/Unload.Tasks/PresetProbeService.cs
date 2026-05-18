using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.Tasks;

/// <summary>
/// Выполняет probe SQL и применяет результат к <see cref="DailyWindowPolicy"/>.
/// Probe станет обычной задачей в Фазе 5; пока остаётся сервисом.
/// </summary>
public class PresetProbeService(
    PresetGateOptions options,
    DailyWindowPolicy dailyWindowPolicy,
    IDatabaseClientFactory databaseClientFactory,
    ILogger<PresetProbeService> logger) : IPresetProbeService
{
    private readonly PresetGateOptions _options = options;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly IDatabaseClientFactory _databaseClientFactory = databaseClientFactory;
    private readonly ILogger<PresetProbeService> _logger = logger;

    public async Task<int> ExecuteAndApplyAsync(CancellationToken cancellationToken)
    {
        var probeResult = await ProbeAsync(cancellationToken);

        _dailyWindowPolicy.StartPolling();
        var changed = _dailyWindowPolicy.ApplyProbeResult(probeResult, DateTimeOffset.UtcNow);

        if (changed)
        {
            _logger.LogInformation("Preset probe applied. ProbeResult: {ProbeResult}", probeResult);
        }

        return probeResult;
    }

    private async Task<int> ProbeAsync(CancellationToken cancellationToken)
    {
        var client = _databaseClientFactory.CreateClient();
        try
        {
            await using var reader = await client.GetDataReaderAsync(_options.ProbeSql, cancellationToken);
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
            if (client is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
