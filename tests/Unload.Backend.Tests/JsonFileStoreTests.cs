using System.Text.Json;
using Microsoft.Extensions.Logging;
using Unload.Store;

namespace Unload.Backend.Tests;

public class JsonFileStoreTests
{
    [Fact]
    public void Save_MaintainsPreviousValidSnapshotAsBackup()
    {
        var scratchDirectory = CreateScratchDirectory();
        try
        {
            var stateFilePath = Path.Combine(scratchDirectory, "state.json");
            var store = CreateStore(stateFilePath);

            store.Save(new TestSnapshot("first"));
            store.Save(new TestSnapshot("second"));

            Assert.Equal("second", ReadSnapshot(stateFilePath).Value);
            Assert.Equal("first", ReadSnapshot($"{stateFilePath}.bak").Value);
        }
        finally
        {
            Directory.Delete(scratchDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_CorruptedPrimaryRecoversBackupAndQuarantinesPrimary()
    {
        var scratchDirectory = CreateScratchDirectory();
        try
        {
            var stateFilePath = Path.Combine(scratchDirectory, "state.json");
            var store = CreateStore(stateFilePath);
            store.Save(new TestSnapshot("first"));
            store.Save(new TestSnapshot("second"));
            File.WriteAllText(stateFilePath, "{ corrupted json");

            var recoveredStore = CreateStore(stateFilePath);
            var recovered = Assert.IsType<TestSnapshot>(recoveredStore.Load());

            Assert.Equal("first", recovered.Value);
            Assert.Equal("first", ReadSnapshot(stateFilePath).Value);
            Assert.Single(Directory.GetFiles(scratchDirectory, "state.json.corrupt-*"));
            var health = recoveredStore.GetHealth();
            Assert.Equal(PersistenceHealthStatus.Recovered, health.Status);
            Assert.True(health.IsWritable);
        }
        finally
        {
            Directory.Delete(scratchDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_CorruptedPrimaryWithoutBackupQuarantinesAndBlocksWrites()
    {
        var scratchDirectory = CreateScratchDirectory();
        try
        {
            var stateFilePath = Path.Combine(scratchDirectory, "state.json");
            const string corruptedJson = "{ corrupted json";
            File.WriteAllText(stateFilePath, corruptedJson);
            var store = CreateStore(stateFilePath);

            Assert.Null(store.Load());

            Assert.False(File.Exists(stateFilePath));
            var quarantinePath = Assert.Single(Directory.GetFiles(scratchDirectory, "state.json.corrupt-*"));
            Assert.Equal(corruptedJson, File.ReadAllText(quarantinePath));
            var health = store.GetHealth();
            Assert.Equal(PersistenceHealthStatus.Corrupted, health.Status);
            Assert.False(health.IsWritable);
            Assert.Throws<PersistenceUnavailableException>(() => store.Save(new TestSnapshot("replacement")));
            Assert.False(File.Exists(stateFilePath));
        }
        finally
        {
            Directory.Delete(scratchDirectory, recursive: true);
        }
    }

    [Fact]
    public void Save_WhenWriteFails_LogsErrorAndRethrows()
    {
        var scratchDirectory = Path.Combine(Path.GetTempPath(), $"unload-json-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDirectory);
        try
        {
            var blockedDirectory = Path.Combine(scratchDirectory, "blocked");
            File.WriteAllText(blockedDirectory, "this path is a file");
            var stateFilePath = Path.Combine(blockedDirectory, "state.json");
            var logger = new RecordingLogger();
            var store = new JsonFileStore<TestSnapshot>(stateFilePath, new JsonSerializerOptions(), logger);

            var exception = Assert.Throws<IOException>(() => store.Save(new TestSnapshot("value")));

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Same(exception, entry.Exception);
            Assert.Contains(stateFilePath, entry.Message);

            var unavailable = Assert.Throws<PersistenceUnavailableException>(store.EnsureWritable);
            Assert.Equal(stateFilePath, unavailable.FilePath);
            Assert.Same(exception, unavailable.InnerException);
            Assert.Throws<PersistenceUnavailableException>(() => store.Save(new TestSnapshot("second")));
            Assert.Single(logger.Entries);
            var health = store.GetHealth();
            Assert.Equal(PersistenceHealthStatus.Degraded, health.Status);
            Assert.False(health.IsWritable);
        }
        finally
        {
            Directory.Delete(scratchDirectory, recursive: true);
        }
    }

    private sealed record TestSnapshot(string Value);

    private static string CreateScratchDirectory()
    {
        var scratchDirectory = Path.Combine(Path.GetTempPath(), $"unload-json-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDirectory);
        return scratchDirectory;
    }

    private static JsonFileStore<TestSnapshot> CreateStore(string stateFilePath)
    {
        return new JsonFileStore<TestSnapshot>(stateFilePath, new JsonSerializerOptions());
    }

    private static TestSnapshot ReadSnapshot(string path)
    {
        return Assert.IsType<TestSnapshot>(
            JsonSerializer.Deserialize<TestSnapshot>(File.ReadAllText(path)));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
}
