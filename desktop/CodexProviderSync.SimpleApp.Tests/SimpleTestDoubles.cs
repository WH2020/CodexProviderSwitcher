using CodexProviderSync.Application;
using CodexProviderSync.Core;
using CodexProviderSync.SimpleApp;

namespace CodexProviderSync.SimpleApp.Tests;

internal static class SimpleSwitcherTestData
{
    internal static StatusSnapshot Status(
        string current,
        IReadOnlyList<string> configured,
        IReadOnlyList<string>? rolloutProviders = null,
        bool sqliteSupported = true,
        bool currentImplicit = false,
        IReadOnlyList<TransactionRecoveryInfo>? pendingTransactions = null) => new()
        {
            CodexHome = @"C:\fixture\.codex",
            CurrentProvider = new CurrentProviderInfo(current, currentImplicit),
            ConfiguredProviders = configured,
            RolloutCounts = new ProviderCounts
            {
                Sessions = (rolloutProviders ?? [])
                    .ToDictionary(item => item, _ => 1, StringComparer.Ordinal)
            },
            LockedRolloutFiles = [],
            UnreadableRolloutFiles = [],
            EncryptedContentCounts = new ProviderCounts(),
            SqliteCounts = new ProviderCounts(),
            SqliteAccess = sqliteSupported
                ? SqliteAccessInfo.Direct
                : new SqliteAccessInfo(false, "unsupported", "SQLite 不支持"),
            BackupRoot = @"C:\fixture\.codex\backups_state\provider-sync",
            BackupSummary = new BackupSummary { Count = 0, TotalBytes = 0 },
            PendingTransactions = pendingTransactions ?? []
        };
}

internal sealed class FakeSimpleProviderService : ISimpleProviderService
{
    private readonly StatusSnapshot _status;

    internal FakeSimpleProviderService(StatusSnapshot status)
    {
        _status = status;
    }

    public Task<StatusSnapshot> GetStatusAsync(string codexHome, CancellationToken cancellationToken = default) =>
        Task.FromResult(_status);

    public Task<SyncResult> ExecuteAsync(ApplicationWriteIntent intent, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class FakeProcessProbe : ICodexProcessProbe
{
    private readonly IReadOnlyList<CodexProcessInfo> _running;

    internal FakeProcessProbe(IReadOnlyList<CodexProcessInfo>? running = null)
    {
        _running = running ?? [];
    }

    public IReadOnlyList<CodexProcessInfo> FindRunning() => _running;
}
