namespace CodexProviderSync.SimpleApp;

internal enum SimpleActivity
{
    Loading,
    Ready,
    Executing,
    Success,
    Incomplete,
    Blocked,
    Failed,
    RecoveryRequired
}

internal sealed record SimpleProviderItem(string Id, bool IsCurrent);

internal sealed record SimpleSyncSummary(
    string TargetProvider,
    int ChangedRolloutFiles,
    int SqliteRowsUpdated,
    int SkippedRolloutFiles,
    string BackupDirectory);

internal sealed record SimpleSwitcherSnapshot
{
    internal SimpleActivity Activity { get; init; } = SimpleActivity.Loading;
    internal string CodexHome { get; init; } = string.Empty;
    internal string? CurrentProviderId { get; init; }
    internal IReadOnlyList<SimpleProviderItem> Providers { get; init; } = [];
    internal string? SelectedProviderId { get; init; }
    internal string Message { get; init; } = "正在读取状态...";
    internal string Details { get; init; } = string.Empty;
    internal string? EncryptedContentWarning { get; init; }
    internal bool CanRefresh { get; init; }
    internal bool CanExecute { get; init; }
    internal SimpleSyncSummary? LastResult { get; init; }
}
