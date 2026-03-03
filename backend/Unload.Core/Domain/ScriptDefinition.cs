namespace Unload.Core;

public record ScriptDefinition(
    string ProfileCode,
    string ScriptCode,
    string OutputFileStem,
    string OutputFileExtension,
    string ScriptPath,
    string SqlText);
