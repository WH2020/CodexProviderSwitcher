using CodexProviderSync.SimpleApp;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class CodexProcessProbeTests
{
    [Theory]
    [InlineData("codex", true)]
    [InlineData("Codex", true)]
    [InlineData("CODEX-APP-SERVER", true)]
    [InlineData("app-server", true)]
    [InlineData("CodexProviderSwitcher", false)]
    [InlineData("ChatGPT", false)]
    [InlineData("powershell", false)]
    public void IsKnownCodexProcess_UsesTheExactAllowlist(
        string processName,
        bool expected)
    {
        Assert.Equal(expected, CodexProcessProbe.IsKnownCodexProcess(processName));
    }

    [Fact]
    public void FindRunning_ExcludesTheSwitcherPidAndSortsResults()
    {
        CodexProcessProbe probe = new(
            currentProcessId: 42,
            snapshot: () =>
            [
                new("app-server", 9),
                new("codex", 42),
                new("powershell", 3),
                new("codex", 7)
            ]);

        Assert.Equal(
            [new CodexProcessInfo("codex", 7), new CodexProcessInfo("app-server", 9)],
            probe.FindRunning());
    }
}
