using System.Text.Json;
using Microsoft.Extensions.Logging;
using Unload.Store;

namespace Unload.Backend.Tests;

public class JsonFileStoreTests
{
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
        }
        finally
        {
            Directory.Delete(scratchDirectory, recursive: true);
        }
    }

    private sealed record TestSnapshot(string Value);

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
