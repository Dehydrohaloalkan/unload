using Unload.Core;

namespace Unload.Runner;

/// <summary>
/// Валидации и guard-проверки для запуска выгрузки.
/// </summary>
internal static class RunnerEngineGuard
{
    public static void ValidateRequest(RunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new InvalidOperationException("CorrelationId is required.");
        }

        if (request.TargetCodes.Count == 0)
        {
            throw new InvalidOperationException("At least one target code is required.");
        }
    }

    public static void ValidateDatabaseConnectivity(IDatabaseClient databaseClient)
    {
        if (!databaseClient.IsConnected)
        {
            throw new InvalidOperationException("Database connection is not available.");
        }
    }

    public static void ValidateOptions(RunnerOptions options)
    {
        if (options.ChunkSizeBytes <= 0)
        {
            throw new InvalidOperationException("ChunkSizeBytes must be greater than zero.");
        }

        if (options.MaxDegreeOfParallelism <= 0)
        {
            throw new InvalidOperationException("MaxDegreeOfParallelism must be greater than zero.");
        }

        if (options.FileWriterDegreeOfParallelism <= 0)
        {
            throw new InvalidOperationException("FileWriterDegreeOfParallelism must be greater than zero.");
        }

        if (options.QueuePublisherDegreeOfParallelism <= 0)
        {
            throw new InvalidOperationException("QueuePublisherDegreeOfParallelism must be greater than zero.");
        }

        if (options.DataflowBoundedCapacity <= 0)
        {
            throw new InvalidOperationException("DataflowBoundedCapacity must be greater than zero.");
        }
    }
}
