using System.Diagnostics.CodeAnalysis;
using Unload.Core;

namespace Unload.Runner;

internal sealed class ScriptDistributor
{
    private readonly Queue<ScriptDefinition> _bigScripts;
    private readonly Queue<ScriptDefinition> _lightScripts;
    private readonly Lock _lock = new();

    public ScriptDistributor(
        IEnumerable<ScriptDefinition> scripts,
        IReadOnlySet<string> bigScriptTargetCodes)
    {
        _bigScripts = new Queue<ScriptDefinition>();
        _lightScripts = new Queue<ScriptDefinition>();

        foreach (var script in scripts)
        {
            if (bigScriptTargetCodes.Contains(script.TargetCode))
                _bigScripts.Enqueue(script);
            else
                _lightScripts.Enqueue(script);
        }
    }

    public bool TryTakeNext(
        WorkerQueuePreference queuePreference,
        [NotNullWhen(true)] out ScriptDefinition? script)
    {
        lock (_lock)
        {
            script = null;
            if (queuePreference == WorkerQueuePreference.BigFirst)
            {
                if (_bigScripts.Count > 0)
                {
                    script = _bigScripts.Dequeue();
                    return true;
                }

                if (_lightScripts.Count > 0)
                {
                    script = _lightScripts.Dequeue();
                    return true;
                }

                return false;
            }

            if (_lightScripts.Count > 0)
            {
                script = _lightScripts.Dequeue();
                return true;
            }

            if (_bigScripts.Count > 0)
            {
                script = _bigScripts.Dequeue();
                return true;
            }

            return false;
        }
    }
}

internal enum WorkerQueuePreference
{
    BigFirst,
    LightFirst
}
