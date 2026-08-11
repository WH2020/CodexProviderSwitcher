using CodexProviderSync.Application;
using CodexProviderSync.Core;

namespace CodexProviderSync.SimpleApp;

internal sealed class SimpleSwitcherController
{
    private readonly ISimpleProviderService _service;
    private readonly ICodexProcessProbe _processProbe;
    private readonly string _codexHome;
    private readonly object _snapshotLock = new();
    private SimpleSwitcherSnapshot _snapshot;
    private int _activeOperations;
    private int _executeInProgress;

    internal SimpleSwitcherController(
        ISimpleProviderService service,
        ICodexProcessProbe processProbe,
        string codexHome)
    {
        _service = service;
        _processProbe = processProbe;
        _codexHome = codexHome;
        _snapshot = new SimpleSwitcherSnapshot { CodexHome = codexHome };
    }

    internal event EventHandler? SnapshotChanged;

    internal SimpleSwitcherSnapshot Snapshot
    {
        get
        {
            lock (_snapshotLock)
            {
                return _snapshot;
            }
        }
    }

    internal async Task RefreshAsync(
        string? preferredProvider = null,
        CancellationToken cancellationToken = default)
    {
        BeginRefresh();

        SimpleSwitcherSnapshot completed = Snapshot;
        try
        {
            StatusSnapshot status = await _service.GetStatusAsync(_codexHome, cancellationToken);
            IReadOnlyList<SimpleProviderItem> providers = BuildProviders(status);
            string? selected = SelectConfiguredProvider(preferredProvider, status.CurrentProvider.Provider, providers);

            if (status.PendingTransactions.Count > 0)
            {
                completed = BuildRecoverySnapshot(status, providers, selected);
            }
            else if (!status.SqliteAccess.Supported)
            {
                completed = BuildBlockedSnapshot(status, providers, selected);
            }
            else
            {
                completed = BuildReadySnapshot(
                    status,
                    providers,
                    selected,
                    _processProbe.FindRunning());
            }
        }
        catch
        {
            completed = Snapshot with
            {
                Activity = SimpleActivity.Failed,
                Message = "读取状态失败。",
                Details = string.Empty,
                CanExecute = false
            };
            throw;
        }
        finally
        {
            CompleteRefresh(completed);
        }
    }

    internal bool SelectProvider(string? providerId)
    {
        SimpleSwitcherSnapshot published;
        lock (_snapshotLock)
        {
            if (string.IsNullOrWhiteSpace(providerId)
                || !_snapshot.Providers.Any(item => string.Equals(item.Id, providerId, StringComparison.Ordinal)))
            {
                return false;
            }
            published = _snapshot with
            {
                SelectedProviderId = providerId,
                CanExecute = CanExecute(_snapshot.Activity, providerId, _snapshot.Details)
            };
            _snapshot = published;
        }
        NotifySnapshotChanged(published);
        return true;
    }

    internal async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _executeInProgress, 1, 0) != 0)
        {
            return;
        }

        SimpleSwitcherSnapshot initial = Snapshot;
        SimpleSwitcherSnapshot completed = initial;
        try
        {
            if (initial.Activity != SimpleActivity.Ready
                || string.IsNullOrWhiteSpace(initial.SelectedProviderId))
            {
                return;
            }

            BeginExecution();
            string selectedProvider = initial.SelectedProviderId;
            IReadOnlyList<CodexProcessInfo> running = _processProbe.FindRunning();
            if (running.Count > 0)
            {
                completed = initial with
                {
                    Activity = SimpleActivity.Blocked,
                    Message = "检测到 Codex 正在运行，请手动关闭后再试。",
                    Details = string.Join(", ", running.Select(item => $"{item.Name} (PID {item.ProcessId})")),
                    CanExecute = false,
                    LastResult = null
                };
                return;
            }

            StatusSnapshot refreshed = await ReadStatusAsync(selectedProvider, cancellationToken);
            IReadOnlyList<SimpleProviderItem> providers = BuildProviders(refreshed);
            if (refreshed.PendingTransactions.Count > 0)
            {
                completed = BuildRecoverySnapshot(refreshed, providers, selectedProvider);
                return;
            }
            if (!refreshed.SqliteAccess.Supported)
            {
                completed = BuildBlockedSnapshot(refreshed, providers, selectedProvider);
                return;
            }
            if (!providers.Any(item => string.Equals(item.Id, selectedProvider, StringComparison.Ordinal)))
            {
                completed = initial with
                {
                    Activity = SimpleActivity.Failed,
                    Message = "所选 Provider 已不在当前配置中，请刷新后重试。",
                    Details = string.Empty,
                    CanExecute = false,
                    LastResult = null
                };
                return;
            }

            ApplicationWriteIntent intent = string.Equals(
                selectedProvider,
                refreshed.CurrentProvider.Provider,
                StringComparison.Ordinal)
                ? new SyncIntent(
                    _codexHome,
                    null,
                    selectedProvider,
                    AppConstants.DefaultBackupRetentionCount)
                : new SwitchIntent(
                    _codexHome,
                    null,
                    selectedProvider,
                    new FollowProviderModelSelection(),
                    AppConstants.DefaultBackupRetentionCount);

            SyncResult result = await _service.ExecuteAsync(intent, cancellationToken);
            SimpleSyncSummary summary = new(
                result.TargetProvider,
                result.ChangedSessionFiles,
                result.SqliteRowsUpdated,
                result.SkippedLockedRolloutFiles.Count + result.SkippedUnreadableRolloutFiles.Count,
                result.BackupDir);
            completed = initial with
            {
                Activity = summary.SkippedRolloutFiles == 0 ? SimpleActivity.Success : SimpleActivity.Incomplete,
                CurrentProviderId = result.TargetProvider,
                Providers = providers,
                SelectedProviderId = selectedProvider,
                Message = summary.SkippedRolloutFiles == 0
                    ? "同步完成，现在可以重新打开 Codex。"
                    : "同步未完全完成，部分会话文件未写入。",
                Details = summary.SkippedRolloutFiles == 0
                    ? result.BackupDir
                    : $"跳过 {summary.SkippedRolloutFiles} 个会话文件，请关闭占用后再次同步。{Environment.NewLine}{result.BackupDir}",
                EncryptedContentWarning = result.EncryptedContentWarning ?? initial.EncryptedContentWarning,
                LastResult = summary,
                CanExecute = false
            };
        }
        catch (SimpleApplicationException exception) when (exception.RecoveryRequired)
        {
            completed = initial with
            {
                Activity = SimpleActivity.RecoveryRequired,
                Message = "操作失败，需要使用备份恢复。",
                Details = FormatApplicationErrors(exception.Errors),
                CanExecute = false,
                LastResult = null
            };
        }
        catch (SimpleApplicationException exception) when (exception.Errors.Any(item =>
            string.Equals(item.Code, "target_busy", StringComparison.Ordinal)))
        {
            completed = initial with
            {
                Activity = SimpleActivity.Blocked,
                Message = "目标文件正在使用，请手动关闭 Codex 后再试。",
                Details = FormatApplicationErrors(exception.Errors),
                CanExecute = false,
                LastResult = null
            };
        }
        catch (Exception exception)
        {
            completed = initial with
            {
                Activity = SimpleActivity.Failed,
                Message = "同步失败。",
                Details = exception.Message,
                CanExecute = false,
                LastResult = null
            };
        }
        finally
        {
            if (Volatile.Read(ref _activeOperations) > 0)
            {
                CompleteExecution(completed);
            }
            Interlocked.Exchange(ref _executeInProgress, 0);
        }
    }

    private Task<StatusSnapshot> ReadStatusAsync(string selectedProvider, CancellationToken cancellationToken) =>
        _service.GetStatusAsync(_codexHome, cancellationToken);

    private static string FormatApplicationErrors(IReadOnlyList<ApplicationError> errors) =>
        string.Join(Environment.NewLine, errors.Select(item =>
            string.IsNullOrWhiteSpace(item.EvidencePath)
                ? item.Message
                : item.Message + Environment.NewLine + item.EvidencePath));

    private SimpleSwitcherSnapshot BuildRecoverySnapshot(
        StatusSnapshot status,
        IReadOnlyList<SimpleProviderItem> providers,
        string? selectedProvider)
    {
        return new SimpleSwitcherSnapshot
        {
            Activity = SimpleActivity.RecoveryRequired,
            CodexHome = _codexHome,
            CurrentProviderId = status.CurrentProvider.Provider,
            Providers = providers,
            SelectedProviderId = selectedProvider,
            Message = "检测到需要恢复的未完成操作。",
            Details = string.Join(Environment.NewLine, status.PendingTransactions.Select(item => item.BackupDirectory)),
            EncryptedContentWarning = status.EncryptedContentWarning,
            CanExecute = false
        };
    }

    private SimpleSwitcherSnapshot BuildBlockedSnapshot(
        StatusSnapshot status,
        IReadOnlyList<SimpleProviderItem> providers,
        string? selectedProvider) => new()
    {
        Activity = SimpleActivity.Blocked,
        CodexHome = _codexHome,
        CurrentProviderId = status.CurrentProvider.Provider,
        Providers = providers,
        SelectedProviderId = selectedProvider,
        Message = "SQLite 不支持，无法执行切换。",
        Details = status.SqliteAccess.Message ?? string.Empty,
        EncryptedContentWarning = status.EncryptedContentWarning,
        CanExecute = false
    };

    private SimpleSwitcherSnapshot BuildReadySnapshot(
        StatusSnapshot status,
        IReadOnlyList<SimpleProviderItem> providers,
        string? selectedProvider,
        IReadOnlyList<CodexProcessInfo> runningProcesses)
    {
        bool processesRunning = runningProcesses.Count > 0;
        string details = processesRunning
            ? string.Join(", ", runningProcesses.Select(item => item.Name + " (" + item.ProcessId + ")"))
            : string.Empty;
        return new SimpleSwitcherSnapshot
        {
            Activity = SimpleActivity.Ready,
            CodexHome = _codexHome,
            CurrentProviderId = status.CurrentProvider.Provider,
            Providers = providers,
            SelectedProviderId = selectedProvider,
            Message = processesRunning ? "检测到 Codex 正在运行，关闭后可执行。" : "状态已就绪。",
            Details = details,
            EncryptedContentWarning = status.EncryptedContentWarning,
            CanExecute = false
        };
    }

    private static IReadOnlyList<SimpleProviderItem> BuildProviders(StatusSnapshot status)
    {
        HashSet<string> configured = new(
            status.ConfiguredProviders.Where(item => !string.IsNullOrWhiteSpace(item)),
            StringComparer.Ordinal);
        if (status.CurrentProvider.Implicit
            && string.Equals(
                status.CurrentProvider.Provider,
                AppConstants.DefaultProvider,
                StringComparison.Ordinal))
        {
            configured.Add(AppConstants.DefaultProvider);
        }
        return configured
            .OrderByDescending(item =>
                string.Equals(item, status.CurrentProvider.Provider, StringComparison.Ordinal))
            .ThenBy(item => item, StringComparer.Ordinal)
            .Select(item => new SimpleProviderItem(
                item,
                string.Equals(item, status.CurrentProvider.Provider, StringComparison.Ordinal)))
            .ToArray()
            .AsReadOnly();
    }

    private static string? SelectConfiguredProvider(
        string? preferredProvider,
        string currentProvider,
        IReadOnlyList<SimpleProviderItem> providers)
    {
        if (providers.Any(item => string.Equals(item.Id, preferredProvider, StringComparison.Ordinal)))
        {
            return preferredProvider;
        }
        if (providers.Any(item => string.Equals(item.Id, currentProvider, StringComparison.Ordinal)))
        {
            return currentProvider;
        }
        return providers.FirstOrDefault()?.Id;
    }

    private bool CanExecute(SimpleActivity activity, string? selectedProvider, string details) =>
        activity == SimpleActivity.Ready
        && !string.IsNullOrWhiteSpace(selectedProvider)
        && string.IsNullOrEmpty(details)
        && Volatile.Read(ref _activeOperations) == 0;

    private void BeginRefresh()
    {
        SimpleSwitcherSnapshot published;
        lock (_snapshotLock)
        {
            Interlocked.Increment(ref _activeOperations);
            published = _snapshot with
            {
                Activity = SimpleActivity.Loading,
                Message = "正在读取状态...",
                Details = string.Empty,
                CanRefresh = false,
                CanExecute = false
            };
            _snapshot = published;
        }
        NotifySnapshotChanged(published);
    }

    private void CompleteRefresh(SimpleSwitcherSnapshot completed)
    {
        SimpleSwitcherSnapshot published;
        lock (_snapshotLock)
        {
            Interlocked.Decrement(ref _activeOperations);
            published = completed with
            {
                CanRefresh = Volatile.Read(ref _activeOperations) == 0,
                CanExecute = CanExecute(
                    completed.Activity,
                    completed.SelectedProviderId,
                    completed.Details)
            };
            _snapshot = published;
        }
        NotifySnapshotChanged(published);
    }

    private void BeginExecution()
    {
        SimpleSwitcherSnapshot published;
        lock (_snapshotLock)
        {
            Interlocked.Increment(ref _activeOperations);
            published = _snapshot with
            {
                Activity = SimpleActivity.Executing,
                Message = "正在同步...",
                Details = string.Empty,
                CanRefresh = false,
                CanExecute = false
            };
            _snapshot = published;
        }
        NotifySnapshotChanged(published);
    }

    private void CompleteExecution(SimpleSwitcherSnapshot completed)
    {
        SimpleSwitcherSnapshot published;
        lock (_snapshotLock)
        {
            Interlocked.Decrement(ref _activeOperations);
            published = completed with
            {
                CanRefresh = Volatile.Read(ref _activeOperations) == 0,
                CanExecute = CanExecute(
                    completed.Activity,
                    completed.SelectedProviderId,
                    completed.Details)
            };
            _snapshot = published;
        }
        NotifySnapshotChanged(published);
    }

    private void NotifySnapshotChanged(SimpleSwitcherSnapshot snapshot)
    {
        Delegate[] handlers = SnapshotChanged?.GetInvocationList() ?? [];
        foreach (EventHandler handler in handlers.Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // Observers must not corrupt the state machine or active operation count.
            }
        }
    }
}
