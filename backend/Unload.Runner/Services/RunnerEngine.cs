using System.Collections.Concurrent;
using System.Threading.Channels;
using Unload.Core;

namespace Unload.Runner;

/// <summary>
/// Реализация движка выгрузки данных.
/// Используется background worker и console для выполнения SQL-скриптов, чанкования результатов и публикации событий.
/// </summary>
public class RunnerEngine : IRunner
{
    private readonly ICatalogService _catalogService;
    private readonly IDatabaseClient _databaseClient;
    private readonly IFileChunkWriter _fileChunkWriter;
    private readonly IMqPublisher _mqPublisher;
    private readonly RunnerOptions _options;

    /// <summary>
    /// Создает экземпляр раннера с инфраструктурными зависимостями.
    /// </summary>
    /// <param name="catalogService">Сервис резолва target-кодов и скриптов.</param>
    /// <param name="databaseClient">Клиент чтения данных из БД.</param>
    /// <param name="fileChunkWriter">Сервис записи чанков в файлы.</param>
    /// <param name="mqPublisher">Публикатор событий раннера в MQ.</param>
    /// <param name="options">Опции чанкования и параллелизма.</param>
    public RunnerEngine(
        ICatalogService catalogService,
        IDatabaseClient databaseClient,
        IFileChunkWriter fileChunkWriter,
        IMqPublisher mqPublisher,
        RunnerOptions options)
    {
        _catalogService = catalogService;
        _databaseClient = databaseClient;
        _fileChunkWriter = fileChunkWriter;
        _mqPublisher = mqPublisher;
        _options = options;
    }

    /// <summary>
    /// Запускает обработку запроса и отдает поток событий выполнения.
    /// </summary>
    /// <param name="request">Параметры запуска выгрузки.</param>
    /// <param name="cancellationToken">Токен отмены выполнения.</param>
    /// <returns>Асинхронный поток событий раннера.</returns>
    public IAsyncEnumerable<RunnerEvent> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<RunnerEvent>(new BoundedChannelOptions(_options.DataflowBoundedCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecutePipelineAsync(request, channel.Writer, cancellationToken);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Выполняет полный пайплайн выгрузки: резолв target-кодов, обработка скриптов и финализация запуска.
    /// </summary>
    /// <param name="request">Запрос запуска.</param>
    /// <param name="writer">Канал, в который пишутся события выполнения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ExecutePipelineAsync(
        RunRequest request,
        ChannelWriter<RunnerEvent> writer,
        CancellationToken cancellationToken)
    {
        string? runOutputDirectory = null;
        var reportRows = new ConcurrentBag<RunReportRow>();

        try
        {
            RunnerEngineGuard.ValidateRequest(request);
            RunnerEngineGuard.ValidateDatabaseConnectivity(_databaseClient);

            runOutputDirectory = RunnerEngineGuard.CreateRunOutputDirectory(request.OutputDirectory);
            var runFilesDirectory = RunnerEngineGuard.CreateRunFilesDirectory(runOutputDirectory);

            await EmitAsync(
                writer,
                request,
                RunnerStep.RequestAccepted,
                "Run request accepted.",
                cancellationToken: cancellationToken);

            var resolvedTargets = await _catalogService.ResolveAsync(request.TargetCodes, cancellationToken);
            await EmitAsync(
                writer,
                request,
                RunnerStep.TargetsResolved,
                $"Targets resolved: {resolvedTargets.Count}.",
                records: resolvedTargets.Count,
                cancellationToken: cancellationToken);

            var scripts = resolvedTargets
                .SelectMany(static x => x.Value)
                .OrderBy(static x => x.TargetCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static x => x.ScriptCode, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var script in scripts)
            {
                await EmitAsync(
                    writer,
                    request,
                    RunnerStep.ScriptDiscovered,
                    $"Discovered script {script.ScriptCode}.",
                    targetCode: script.TargetCode,
                    scriptCode: script.ScriptCode,
                    cancellationToken: cancellationToken);
            }

            if (scripts.Length == 0)
            {
                await EmitAsync(
                    writer,
                    request,
                    RunnerStep.Completed,
                    "No scripts found for selected targets.",
                    cancellationToken: cancellationToken);
                return;
            }

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism
            };

            await Parallel.ForEachAsync(scripts, parallelOptions, async (script, token) =>
            {
                await ProcessScriptAsync(request, script, runFilesDirectory, writer, reportRows, token);
            });

            await EmitAsync(
                writer,
                request,
                RunnerStep.Completed,
                $"Run completed successfully. Output: {runOutputDirectory}",
                filePath: runOutputDirectory,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await EmitAsync(
                writer,
                request,
                RunnerStep.Failed,
                "Run was cancelled.",
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            await EmitAsync(
                writer,
                request,
                RunnerStep.Failed,
                ex.Message,
                cancellationToken: CancellationToken.None);
        }
        finally
        {
            if (runOutputDirectory is not null)
            {
                var reportPath = Path.Combine(runOutputDirectory, RunnerEngineGuard.RunReportFileName);
                await RunReportCsvWriter.WriteAsync(reportPath, reportRows, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Выполняет один SQL-скрипт: читает данные, формирует чанки и отправляет их на запись.
    /// </summary>
    /// <param name="request">Исходный запрос запуска.</param>
    /// <param name="script">Определение скрипта для выполнения.</param>
    /// <param name="runOutputDirectory">Директория результатов текущего запуска.</param>
    /// <param name="writer">Канал публикации событий.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ProcessScriptAsync(
        RunRequest request,
        ScriptDefinition script,
        string runFilesDirectory,
        ChannelWriter<RunnerEvent> writer,
        ConcurrentBag<RunReportRow> reportRows,
        CancellationToken cancellationToken)
    {
        await EmitAsync(
            writer,
            request,
            RunnerStep.QueryStarted,
            $"Running query for script {script.ScriptCode}.",
            targetCode: script.TargetCode,
            scriptCode: script.ScriptCode,
            cancellationToken: cancellationToken);

        await using var reader = await _databaseClient.GetDataReaderAsync(script.SqlText, cancellationToken);
        var columns = RunnerEngineDataReader.GetColumns(reader);

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Query for script '{script.ScriptCode}' returned no columns.");
        }

        var rowsRead = 0;
        var chunkNumber = 1;
        var currentRows = new List<DatabaseRow>();
        var headerLine =
            $"#|{script.ScriptType}|{script.OutputFileStem}{DateTimeOffset.Now.DayOfYear}{chunkNumber}{script.OutputFileExtension}|{OutputFormatConstants.SenderCode}|{DateTimeOffset.Now:yyyy-MM-dd}|{int.MaxValue}|{script.FirstCodeDigit}";
        var headerSize = PipeDelimitedFormatter.EstimateLineBytes(headerLine);
        var currentSize = headerSize;

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = RunnerEngineDataReader.ReadRow(reader, columns);
            var line = PipeDelimitedFormatter.BuildDataLine(row, columns);
            var rowSize = PipeDelimitedFormatter.EstimateLineBytes(line);
            if (rowSize + headerSize > _options.ChunkSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Single row size {rowSize} bytes exceeds chunk size {_options.ChunkSizeBytes} bytes.");
            }

            if (currentRows.Count > 0 && currentSize + rowSize > _options.ChunkSizeBytes)
            {
                await FlushChunkAsync(
                    request,
                    script,
                    runFilesDirectory,
                    writer,
                    chunkNumber,
                    currentRows,
                    currentSize,
                    reportRows,
                    cancellationToken);
                chunkNumber++;
                currentRows = [];
                currentSize = headerSize;
            }

            currentRows.Add(row);
            currentSize += rowSize;
            rowsRead++;
        }

        await EmitAsync(
            writer,
            request,
            RunnerStep.QueryCompleted,
            $"Query finished for script {script.ScriptCode}.",
            targetCode: script.TargetCode,
            scriptCode: script.ScriptCode,
            records: rowsRead,
            cancellationToken: cancellationToken);

        if (rowsRead == 0)
        {
            return;
        }

        if (currentRows.Count > 0)
        {
            await FlushChunkAsync(
                request,
                script,
                runFilesDirectory,
                writer,
                chunkNumber,
                currentRows,
                currentSize,
                reportRows,
                cancellationToken);
        }

    }

    /// <summary>
    /// Формирует объект чанка, записывает его в файл и публикует соответствующие события.
    /// </summary>
    /// <param name="request">Запрос запуска.</param>
    /// <param name="script">Скрипт, к которому относится чанк.</param>
    /// <param name="runOutputDirectory">Директория результатов текущего запуска.</param>
    /// <param name="writer">Канал публикации событий.</param>
    /// <param name="chunkNumber">Порядковый номер чанка.</param>
    /// <param name="rows">Строки данных чанка.</param>
    /// <param name="byteSize">Размер чанка в байтах.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task FlushChunkAsync(
        RunRequest request,
        ScriptDefinition script,
        string runFilesDirectory,
        ChannelWriter<RunnerEvent> writer,
        int chunkNumber,
        IReadOnlyList<DatabaseRow> rows,
        int byteSize,
        ConcurrentBag<RunReportRow> reportRows,
        CancellationToken cancellationToken)
    {
        var chunk = new FileChunk(script, chunkNumber, rows.ToArray(), byteSize);
        await EmitAsync(
            writer,
            request,
            RunnerStep.ChunkCreated,
            $"Chunk #{chunk.ChunkNumber} created for {script.ScriptCode}.",
            targetCode: script.TargetCode,
            scriptCode: script.ScriptCode,
            records: chunk.Rows.Count,
            cancellationToken: cancellationToken);

        var written = await _fileChunkWriter.WriteChunkAsync(chunk, runFilesDirectory, cancellationToken);
        var isSentToMq = await EmitAsync(
            writer,
            request,
            RunnerStep.FileWritten,
            $"File written: {Path.GetFileName(written.FilePath)}.",
            targetCode: script.TargetCode,
            scriptCode: script.ScriptCode,
            records: written.RowsCount,
            filePath: written.FilePath,
            cancellationToken: cancellationToken);

        reportRows.Add(new RunReportRow(
            script.MemberName,
            script.ScriptType,
            script.FirstCodeDigit,
            Path.GetFileName(written.FilePath),
            written.RowsCount,
            isSentToMq));
    }

    /// <summary>
    /// Создает событие раннера и доставляет его в MQ и поток клиентских событий.
    /// </summary>
    /// <param name="writer">Канал публикации событий раннера.</param>
    /// <param name="request">Запрос запуска.</param>
    /// <param name="step">Шаг выполнения.</param>
    /// <param name="message">Текст события.</param>
    /// <param name="targetCode">Target-код (опционально).</param>
    /// <param name="scriptCode">Код скрипта (опционально).</param>
    /// <param name="records">Количество записей (опционально).</param>
    /// <param name="filePath">Путь к файлу результата (опционально).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task<bool> EmitAsync(
        ChannelWriter<RunnerEvent> writer,
        RunRequest request,
        RunnerStep step,
        string message,
        string? targetCode = null,
        string? scriptCode = null,
        int? records = null,
        string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        var @event = new RunnerEvent(
            DateTimeOffset.UtcNow,
            request.CorrelationId,
            step,
            message,
            targetCode,
            scriptCode,
            records,
            filePath);

        var isSentToMq = await TryPublishToMqAsync(@event, cancellationToken);
        await writer.WriteAsync(@event, cancellationToken);
        return isSentToMq;
    }

    private async Task<bool> TryPublishToMqAsync(RunnerEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await _mqPublisher.PublishAsync(@event, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

}
