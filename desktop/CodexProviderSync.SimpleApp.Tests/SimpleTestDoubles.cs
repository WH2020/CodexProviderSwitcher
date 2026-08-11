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

internal sealed class ThrowingProcessProbe : ICodexProcessProbe
{
    public IReadOnlyList<CodexProcessInfo> FindRunning() =>
        throw new InvalidOperationException("Process probing must not occur for a blocked state.");
}

internal sealed class BlockingStatusProviderService : ISimpleProviderService
{
    private readonly StatusSnapshot _first;
    private readonly StatusSnapshot _second;
    private readonly ManualResetEventSlim _secondRequested = new();
    private readonly TaskCompletionSource _releaseSecond = new();
    private int _requests;

    internal BlockingStatusProviderService(StatusSnapshot first, StatusSnapshot second)
    {
        _first = first;
        _second = second;
    }

    public async Task<StatusSnapshot> GetStatusAsync(string codexHome, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _requests) == 1)
        {
            return _first;
        }
        _secondRequested.Set();
        await _releaseSecond.Task.WaitAsync(cancellationToken);
        return _second;
    }

    public Task<SyncResult> ExecuteAsync(ApplicationWriteIntent intent, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    internal bool WaitForSecondRequest(TimeSpan timeout) => _secondRequested.Wait(timeout);

    internal bool ReleaseSecondRequest() => _releaseSecond.TrySetResult();
}

internal sealed class GateStatusProviderService : ISimpleProviderService
{
    private readonly StatusSnapshot _status;
    private readonly ManualResetEventSlim _requested = new();
    private readonly TaskCompletionSource _release = new();

    internal GateStatusProviderService(StatusSnapshot status)
    {
        _status = status;
    }

    public async Task<StatusSnapshot> GetStatusAsync(string codexHome, CancellationToken cancellationToken = default)
    {
        _requested.Set();
        await _release.Task.WaitAsync(cancellationToken);
        return _status;
    }

    public Task<SyncResult> ExecuteAsync(ApplicationWriteIntent intent, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    internal bool WaitForRequest(TimeSpan timeout) => _requested.Wait(timeout);
    internal bool Release() => _release.TrySetResult();
}

internal sealed class TriggeringProviderList : IReadOnlyList<SimpleProviderItem>
{
    private readonly IReadOnlyList<SimpleProviderItem> _items;
    private readonly Action _onEnumerate;
    private int _triggered;

    internal TriggeringProviderList(IReadOnlyList<SimpleProviderItem> items, Action onEnumerate)
    {
        _items = items;
        _onEnumerate = onEnumerate;
    }

    public SimpleProviderItem this[int index] => _items[index];
    public int Count => _items.Count;

    public IEnumerator<SimpleProviderItem> GetEnumerator()
    {
        if (Interlocked.Exchange(ref _triggered, 1) == 0)
        {
            _onEnumerate();
        }
        return _items.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
