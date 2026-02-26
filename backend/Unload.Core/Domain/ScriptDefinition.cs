namespace Unload.Core;

public sealed record ScriptDefinition(
    string ProfileCode,
    string ScriptCode,
    string ScriptPath,
    string SqlText);
