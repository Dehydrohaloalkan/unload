using System.Data;
using System.Data.Common;
using Unload.Core;
using Unload.Tasks.MainUnload;

namespace Unload.Backend.Tests;

/// <summary>
/// Детерминированно ломает внешние зависимости основной выгрузки.
/// Все файлы создаются только в уникальном каталоге системного temp.
/// </summary>
public sealed class MainUnloadChaosTests
{
    [Theory]
    [InlineData(ChaosPoint.Catalog, "catalog unavailable")]
    [InlineData(ChaosPoint.DatabaseQuery, "database query refused")]
    [InlineData(ChaosPoint.FileWrite, "disk write failed")]
    [InlineData(ChaosPoint.GatewayPublish, "gateway publish failed")]
    public async Task FailureAfterEventStreamStarted_EndsWithFailedEvent(
        ChaosPoint chaosPoint,
        string expectedMessage)
    {
        using var scratch = new ScratchDirectory();
        var engine = CreateEngine(chaosPoint);

        var events = await CollectAsync(engine.RunAsync(
            Request(scratch.Path, publishToGateway: chaosPoint == ChaosPoint.GatewayPublish),
            CancellationToken.None));

        var failed = Assert.Single(events, static item => item.Step == RunnerStep.Failed);
        Assert.Contains(expectedMessage, failed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(events, static item => item.Step == RunnerStep.Completed);
    }

    [Fact]
    public async Task DisconnectedDatabaseBeforeEventStream_CurrentlyProducesNoTerminalEvent()
    {
        using var scratch = new ScratchDirectory();
        var engine = new MainUnloadEngine(
            new SingleScriptCatalog(),
            new TestDatabaseFactory(new DisconnectedDatabaseClient()),
            new TestFileWriter(),
            new TestGatewayPublisher(),
            new RunnerOptions(ChunkSizeBytes: 4096, WorkerCount: 1));

        var events = await CollectAsync(engine.RunAsync(Request(scratch.Path), CancellationToken.None));

        // Safety canary: this documents a known gap. MainUnloadHostedService has already changed
        // the aggregate to Running, but the engine cannot emit Failed because its emitter is
        // created only after this connectivity check.
        Assert.Empty(events);
    }

    [Fact]
    public async Task UnwritableOutputRootBeforeEventStream_CurrentlyProducesNoTerminalEvent()
    {
        using var scratch = new ScratchDirectory();
        var blockedOutputRoot = System.IO.Path.Combine(scratch.Path, "output-is-a-file");
        await File.WriteAllTextAsync(blockedOutputRoot, "not a directory");
        var engine = CreateEngine(ChaosPoint.None);

        var events = await CollectAsync(engine.RunAsync(Request(blockedOutputRoot), CancellationToken.None));

        // Same pre-emitter gap as the disconnected database case above.
        Assert.Empty(events);
    }

    [Fact]
    public async Task CancellationDuringDatabaseQuery_CancelsEventStreamForHostedService()
    {
        using var scratch = new ScratchDirectory();
        using var cancellation = new CancellationTokenSource();
        var database = new BlockingQueryDatabaseClient();
        var engine = new MainUnloadEngine(
            new SingleScriptCatalog(),
            new TestDatabaseFactory(database),
            new TestFileWriter(),
            new TestGatewayPublisher(),
            new RunnerOptions(ChunkSizeBytes: 4096, WorkerCount: 1));

        var run = CollectAsync(engine.RunAsync(Request(scratch.Path), cancellation.Token));
        await database.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task GatewayFailure_LeavesWrittenArtifactAndEndsWithFailedEvent()
    {
        using var scratch = new ScratchDirectory();
        var engine = CreateEngine(ChaosPoint.GatewayPublish);

        var events = await CollectAsync(engine.RunAsync(
            Request(scratch.Path, publishToGateway: true),
            CancellationToken.None));

        var written = Assert.Single(events, static item => item.Step == RunnerStep.FileWritten);
        Assert.NotNull(written.FilePath);
        Assert.True(File.Exists(written.FilePath));
        Assert.Contains(events, static item => item.Step == RunnerStep.Failed);
        Assert.DoesNotContain(events, static item => item.Step == RunnerStep.Completed);
    }

    private static MainUnloadEngine CreateEngine(ChaosPoint chaosPoint)
    {
        ICatalogService catalog = chaosPoint == ChaosPoint.Catalog
            ? new FailingCatalog("catalog unavailable")
            : new SingleScriptCatalog();
        IDatabaseClient database = chaosPoint == ChaosPoint.DatabaseQuery
            ? new ThrowingQueryDatabaseClient("database query refused")
            : new OneRowDatabaseClient();
        IFileChunkWriter writer = chaosPoint == ChaosPoint.FileWrite
            ? new FailingFileWriter("disk write failed")
            : new TestFileWriter();
        IGatewayPublisher gateway = chaosPoint == ChaosPoint.GatewayPublish
            ? new FailingGatewayPublisher("gateway publish failed")
            : new TestGatewayPublisher();

        return new MainUnloadEngine(
            catalog,
            new TestDatabaseFactory(database),
            writer,
            gateway,
            new RunnerOptions(ChunkSizeBytes: 4096, WorkerCount: 1));
    }

    private static RunRequest Request(string outputDirectory, bool publishToGateway = false)
    {
        return new RunRequest(["TARGET-1"], "chaos-run-1", outputDirectory, publishToGateway);
    }

    private static async Task<IReadOnlyList<RunnerEvent>> CollectAsync(
        IAsyncEnumerable<RunnerEvent> source)
    {
        var events = new List<RunnerEvent>();
        await foreach (var item in source)
        {
            events.Add(item);
        }

        return events;
    }

    public enum ChaosPoint
    {
        None,
        Catalog,
        DatabaseQuery,
        FileWrite,
        GatewayPublish
    }

    private sealed class SingleScriptCatalog : ICatalogService
    {
        private static readonly ScriptDefinition Script = new(
            TargetCode: "TARGET-1",
            ScriptCode: "CHAOS_1",
            OutputFileStem: "CHA",
            OutputFileExtension: ".txt",
            ScriptType: "CHAOS",
            ScriptCodes: "1",
            FirstCodeDigit: 1,
            MemberName: "Chaos member",
            ScriptPath: "chaos.sql",
            SqlText: "SELECT CHAOS");

        public Task<CatalogInfo> GetCatalogAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<(
            IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>> Scripts,
            IReadOnlySet<string> BigScriptTargetCodes)> ResolveAsync(
            IReadOnlyCollection<string> targetCodes,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>> scripts =
                new Dictionary<string, IReadOnlyList<ScriptDefinition>>
                {
                    ["TARGET-1"] = [Script]
                };
            return Task.FromResult((scripts, (IReadOnlySet<string>)new HashSet<string>()));
        }
    }

    private sealed class FailingCatalog(string message) : ICatalogService
    {
        public Task<CatalogInfo> GetCatalogAsync(CancellationToken cancellationToken)
        {
            throw new IOException(message);
        }

        public Task<(
            IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>> Scripts,
            IReadOnlySet<string> BigScriptTargetCodes)> ResolveAsync(
            IReadOnlyCollection<string> targetCodes,
            CancellationToken cancellationToken)
        {
            throw new IOException(message);
        }
    }

    private sealed class TestDatabaseFactory(IDatabaseClient database) : IDatabaseClientFactory
    {
        public IDatabaseClient CreateClient() => database;
    }

    private sealed class DisconnectedDatabaseClient : IDatabaseClient
    {
        public bool IsConnected => false;

        public Task<DbDataReader> GetDataReaderAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Query must not start while disconnected.");
        }
    }

    private sealed class ThrowingQueryDatabaseClient(string message) : IDatabaseClient
    {
        public bool IsConnected => true;

        public Task<DbDataReader> GetDataReaderAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            throw new IOException(message);
        }
    }

    private sealed class BlockingQueryDatabaseClient : IDatabaseClient
    {
        public TaskCompletionSource QueryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => true;

        public async Task<DbDataReader> GetDataReaderAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            QueryStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Blocking query unexpectedly resumed.");
        }
    }

    private sealed class OneRowDatabaseClient : IDatabaseClient
    {
        public bool IsConnected => true;

        public Task<DbDataReader> GetDataReaderAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            var table = new DataTable();
            table.Columns.Add("value", typeof(string));
            table.Rows.Add("chaos-row");
            return Task.FromResult<DbDataReader>(table.CreateDataReader());
        }
    }

    private sealed class TestFileWriter : IFileChunkWriter
    {
        public async Task<WrittenFile> WriteChunkAsync(
            FileChunk chunk,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var path = System.IO.Path.Combine(outputDirectory, "chaos.txt");
            await File.WriteAllTextAsync(path, "chaos", cancellationToken);
            return new WrittenFile(
                chunk.Script,
                chunk.ChunkNumber,
                path,
                chunk.Rows.Count,
                chunk.ByteSize);
        }
    }

    private sealed class FailingFileWriter(string message) : IFileChunkWriter
    {
        public Task<WrittenFile> WriteChunkAsync(
            FileChunk chunk,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            throw new IOException(message);
        }
    }

    private class TestGatewayPublisher : IGatewayPublisher
    {
        public virtual Task PublishFileBatchReadyAsync(
            SenderFileBatchReadyEvent @event,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishSenderFeedbackAsync(
            SenderFileDispatchFeedback feedback,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingGatewayPublisher(string message) : TestGatewayPublisher
    {
        public override Task PublishFileBatchReadyAsync(
            SenderFileBatchReadyEvent @event,
            CancellationToken cancellationToken)
        {
            throw new IOException(message);
        }
    }

    private sealed class ScratchDirectory : IDisposable
    {
        public ScratchDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"unload-chaos-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
