namespace Unload.Core;

public enum RunnerStep
{
    RequestAccepted,
    ProfilesResolved,
    ScriptDiscovered,
    QueryStarted,
    QueryCompleted,
    ChunkCreated,
    FileWritten,
    PublishedToMq,
    Completed,
    Failed
}
