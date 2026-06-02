using System.Text;
using Unload.Core;

namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Пишет результаты extra-выгрузки в формате, близком к основной выгрузке:
/// файлы делятся по размеру, имя строится из первых символов кода скрипта,
/// у каждого файла служебный заголовок. Раскладка: <c>output-files/{script}/{bank}/{file}</c>.
/// В шлюз отправляется одна партия на скрипт (MemberName = код скрипта).
/// </summary>
public class ExtraOutputWriter(ExtraUnloadOptions options, IGatewayPublisher gatewayPublisher)
{
    private readonly ExtraUnloadOptions _options = options;
    private readonly IGatewayPublisher _gatewayPublisher = gatewayPublisher;

    public async Task<ExtraOutputWriteResult> WriteAsync(
        string baseOutputDirectory,
        string correlationId,
        IReadOnlyList<ExtraScriptExecutionResult> scriptResults,
        bool publishToGateway,
        CancellationToken cancellationToken)
    {
        var (runDirectory, filesDirectory) = CreateRunDirectory(baseOutputDirectory);
        var filesWritten = 0;

        foreach (var script in scriptResults.OrderBy(static s => s.ScriptCode, StringComparer.OrdinalIgnoreCase))
        {
            var result = await WriteScriptAsync(filesDirectory, correlationId, script, publishToGateway, cancellationToken);
            filesWritten += result.FilesWritten;
        }

        return new ExtraOutputWriteResult(runDirectory, filesWritten);
    }

    /// <summary>
    /// Создаёт каталог запуска и подкаталог <c>output-files</c>; возвращает оба пути.
    /// Используется фоновым движком (<c>ExtraUnloadEngine</c>), который пишет скрипты по одному.
    /// </summary>
    public (string RunDirectory, string FilesDirectory) CreateRunDirectory(string baseOutputDirectory)
    {
        var runDirectory = CreateRunDirectoryCore(baseOutputDirectory);
        var filesDirectory = Path.Combine(runDirectory, "output-files");
        Directory.CreateDirectory(filesDirectory);
        return (runDirectory, filesDirectory);
    }

    /// <summary>
    /// Пишет файлы одного extra-скрипта в общий каталог <paramref name="filesDirectory"/>
    /// и (опционально) публикует партию в шлюз. Возвращает число файлов и их дескрипторы.
    /// </summary>
    public async Task<ExtraScriptWriteResult> WriteScriptAsync(
        string filesDirectory,
        string correlationId,
        ExtraScriptExecutionResult script,
        bool publishToGateway,
        CancellationToken cancellationToken)
    {
        var dayOfYear = DateTimeOffset.Now.DayOfYear;
        var scriptSegment = SanitizeFileNameSegment(script.ScriptCode);
        var stem = BuildStem(script.ScriptCode);
        var scriptDirectory = Path.Combine(filesDirectory, scriptSegment);

        var scriptFiles = new List<SenderFileDescriptor>();
        // Сквозная нумерация чанков в рамках скрипта (по всем банкам).
        var chunkNumber = 0;

        foreach (var bank in script.LinesByBank.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var bankSegment = SanitizeFileNameSegment(bank.Key);
            var bankDirectory = Path.Combine(scriptDirectory, bankSegment);
            Directory.CreateDirectory(bankDirectory);

            // Расширение файла — код банка (NrBank), напр. ~YW15201.B01
            var fileExtension = $".{bankSegment}";

            foreach (var chunkLines in SplitIntoChunks(bank.Value, stem, dayOfYear, fileExtension))
            {
                chunkNumber++;
                var descriptor = await WriteChunkAsync(
                    bankDirectory,
                    stem,
                    dayOfYear,
                    chunkNumber,
                    fileExtension,
                    chunkLines,
                    cancellationToken);
                scriptFiles.Add(descriptor);
            }
        }

        if (publishToGateway && scriptFiles.Count > 0)
        {
            var @event = new SenderFileBatchReadyEvent(
                OccurredAt: DateTimeOffset.UtcNow,
                CorrelationId: correlationId,
                MemberName: script.ScriptCode,
                BatchId: $"{correlationId}:{script.ScriptCode}",
                Version: 1,
                Files: scriptFiles
                    .OrderBy(static f => f.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            await _gatewayPublisher.PublishFileBatchReadyAsync(@event, cancellationToken);
        }

        return new ExtraScriptWriteResult(scriptFiles.Count, scriptFiles);
    }

    /// <summary>
    /// Делит строки банка на чанки, не превышающие <see cref="ExtraUnloadOptions.ChunkSizeBytes"/>
    /// (с учётом размера заголовка).
    /// </summary>
    private IEnumerable<List<string>> SplitIntoChunks(IReadOnlyList<string> lines, string stem, int dayOfYear, string fileExtension)
    {
        // Заголовок оцениваем по максимальным значениям (как в основной выгрузке),
        // чтобы порог чанка учитывал его длину.
        var headerEstimate = BuildHeaderLine(
            OutputFileNaming.BuildChunkBaseName(stem, dayOfYear, 1) + fileExtension,
            int.MaxValue);
        var headerSize = PipeDelimitedFormatter.EstimateLineBytes(headerEstimate);

        var current = new List<string>();
        var currentSize = headerSize;

        foreach (var line in lines)
        {
            var lineSize = PipeDelimitedFormatter.EstimateLineBytes(line);
            if (current.Count > 0 && currentSize + lineSize > _options.ChunkSizeBytes)
            {
                yield return current;
                current = [];
                currentSize = headerSize;
            }

            current.Add(line);
            currentSize += lineSize;
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private async Task<SenderFileDescriptor> WriteChunkAsync(
        string bankDirectory,
        string stem,
        int dayOfYear,
        int chunkNumber,
        string fileExtension,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        var baseFileName = OutputFileNaming.BuildChunkBaseName(stem, dayOfYear, chunkNumber);
        var (stream, fileName, filePath) = OutputFileNaming.OpenUniqueFile(bankDirectory, baseFileName, fileExtension);

        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            await writer.WriteLineAsync(BuildHeaderLine(fileName, lines.Count));
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(line);
            }

            await writer.FlushAsync(cancellationToken);
        }

        var fileInfo = new FileInfo(filePath);
        return new SenderFileDescriptor(
            FilePath: filePath,
            FileName: fileInfo.Name,
            SizeBytes: fileInfo.Length);
    }

    // Тот же формат заголовка, что и в основной выгрузке (см. PipeSeparatedFileChunkWriter).
    // TODO: заполнить тип скрипта (2-й сегмент) и первую цифру кода (последний сегмент) для extra,
    //       когда определят правила их формирования. Пока оставлены пустыми.
    private static string BuildHeaderLine(string fileName, int rowCount)
    {
        const string scriptType = "";
        const string firstCodeDigit = "";
        return $"#|{scriptType}|{fileName}|{OutputFormatConstants.SenderCode}|{DateTimeOffset.Now:yyyy-MM-dd}|{rowCount}|{firstCodeDigit}";
    }

    private static string BuildStem(string scriptCode)
    {
        var normalized = SanitizeFileNameSegment(scriptCode).ToUpperInvariant();
        return normalized.Length <= 3 ? normalized : normalized[..3];
    }

    private string CreateRunDirectoryCore(string baseOutputDirectory)
    {
        Directory.CreateDirectory(baseOutputDirectory);
        var timestamp = DateTime.Now.ToString("dd_MM_yyyy_HHmmss");
        var candidate = Path.Combine(baseOutputDirectory, $"{timestamp}_extra");
        var index = 1;

        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(baseOutputDirectory, $"{timestamp}_extra_{index:D2}");
            index++;
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static string SanitizeFileNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "UNKNOWN" : sanitized;
    }
}
