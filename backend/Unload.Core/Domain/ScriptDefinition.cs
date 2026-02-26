namespace Unload.Core;

public record ScriptDefinition(
    string ProfileCode,
    string ScriptCode,
    string ScriptPath,
    string SqlText);
