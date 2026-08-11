using System.ComponentModel;
using System.Diagnostics;

namespace CodexProviderSync.SimpleApp;

internal sealed record CodexProcessInfo(string Name, int ProcessId);

internal interface ICodexProcessProbe
{
    IReadOnlyList<CodexProcessInfo> FindRunning();
}

internal sealed class CodexProcessProbe : ICodexProcessProbe
{
    private static readonly HashSet<string> KnownNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "codex",
            "codex-app-server",
            "app-server"
        };

    private readonly int _currentProcessId;
    private readonly Func<IReadOnlyList<CodexProcessInfo>> _snapshot;

    internal CodexProcessProbe()
        : this(Environment.ProcessId, CaptureProcessSnapshot)
    {
    }

    internal CodexProcessProbe(
        int currentProcessId,
        Func<IReadOnlyList<CodexProcessInfo>> snapshot)
    {
        _currentProcessId = currentProcessId;
        _snapshot = snapshot;
    }

    internal static bool IsKnownCodexProcess(string processName) =>
        KnownNames.Contains(processName);

    public IReadOnlyList<CodexProcessInfo> FindRunning() =>
        _snapshot()
            .Where(process => process.ProcessId != _currentProcessId)
            .Where(process => IsKnownCodexProcess(process.Name))
            .GroupBy(process => process.ProcessId)
            .Select(group => group.First())
            .OrderBy(process => process.ProcessId)
            .ToArray();

    private static IReadOnlyList<CodexProcessInfo> CaptureProcessSnapshot()
    {
        List<CodexProcessInfo> processes = [];

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    processes.Add(new CodexProcessInfo(process.ProcessName, process.Id));
                }
                catch (InvalidOperationException)
                {
                }
                catch (Win32Exception)
                {
                }
            }
        }

        return processes;
    }
}
