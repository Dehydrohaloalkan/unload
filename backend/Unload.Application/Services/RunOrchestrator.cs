using System.Text.RegularExpressions;
using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Оркестратор постановки запуска выгрузки в очередь.
/// Используется API/Console для валидации target-кодов, создания запроса и публикации начального статуса.
/// </summary>
public class RunOrchestrator : IRunOrchestrator
{
    private static readonly Regex TargetCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);

    private readonly IRunRequestFactory _requestFactory;
    private readonly IRunQueue _runQueue;
    private readonly IRunStateStore _runStateStore;
    private readonly string _outputDirectory;

    /// <summary>
    /// Инициализирует orchestrator с зависимостями очереди, фабрики и хранилища статусов.
    /// </summary>
    /// <param name="requestFactory">Фабрика создания запроса запуска.</param>
    /// <param name="runQueue">Очередь запусков.</param>
    /// <param name="runStateStore">Хранилище статусов запусков.</param>
    /// <param name="outputDirectory">Базовая директория для результатов.</param>
    public RunOrchestrator(
        IRunRequestFactory requestFactory,
        IRunQueue runQueue,
        IRunStateStore runStateStore,
        string outputDirectory)
    {
        _requestFactory = requestFactory;
        _runQueue = runQueue;
        _runStateStore = runStateStore;
        _outputDirectory = Path.GetFullPath(outputDirectory);
    }

    /// <summary>
    /// Нормализует и валидирует target-коды, создает запрос и ставит его в очередь.
    /// </summary>
    /// <param name="targetCodes">Target-коды от клиента.</param>
    /// <returns>Correlation id созданного запуска.</returns>
    public string StartRun(IReadOnlyCollection<string> targetCodes)
    {
        var normalizedCodes = NormalizeTargetCodes(targetCodes);
        var request = _requestFactory.Create(normalizedCodes, _outputDirectory);
        _runStateStore.SetQueued(request.CorrelationId, normalizedCodes);
        if (!_runQueue.TryEnqueue(request))
        {
            throw new InvalidOperationException("Run queue is full. Please retry later.");
        }

        return request.CorrelationId;
    }

    /// <summary>
    /// Выполняет нормализацию и базовую валидацию target-кодов.
    /// </summary>
    /// <param name="targetCodes">Исходный список target-кодов.</param>
    /// <returns>Нормализованный уникальный список кодов в верхнем регистре.</returns>
    private static IReadOnlyCollection<string> NormalizeTargetCodes(IReadOnlyCollection<string> targetCodes)
    {
        var normalized = targetCodes
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("At least one target code is required.");
        }

        foreach (var code in normalized)
        {
            if (!TargetCodePattern.IsMatch(code))
            {
                throw new InvalidOperationException($"Target code '{code}' is invalid.");
            }
        }

        return normalized;
    }
}
