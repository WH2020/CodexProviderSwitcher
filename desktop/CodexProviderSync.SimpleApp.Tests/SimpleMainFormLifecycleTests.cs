using CodexProviderSync.Application;
using CodexProviderSync.Core;
using CodexProviderSync.SimpleApp;
using System.Runtime.InteropServices;
using static CodexProviderSync.SimpleApp.Tests.SimpleSwitcherTestData;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class SimpleMainFormLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingDuringExecuteIsCancelledUntilTheOperationCompletes(bool recoveryRequired)
    {
        GatedExecuteProviderService service = new(recoveryRequired);
        SimpleSwitcherController controller = Controller(service);
        await controller.RefreshAsync();
        int saves = 0;
        using SimpleMainForm form = Form(
            controller,
            settingsSaver: (_, _) =>
            {
                saves++;
                return Task.CompletedTask;
            });
        form.Show();
        System.Windows.Forms.Application.DoEvents();

        Task execution = controller.ExecuteAsync();
        Assert.Equal(SimpleActivity.Executing, controller.Snapshot.Activity);

        form.Close();

        Assert.False(form.IsDisposed);
        Assert.True(form.Visible);
        Assert.Contains("操作完成后再关闭", StateLabel(form).Text);
        Assert.Equal(0, saves);

        service.Release();
        PumpUntil(() => execution.IsCompleted);
        Assert.True(execution.IsCompletedSuccessfully);
        Assert.Equal(
            recoveryRequired ? SimpleActivity.RecoveryRequired : SimpleActivity.Success,
            controller.Snapshot.Activity);

        form.Close();

        Assert.True(form.IsDisposed);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void Shown_LoadFailureFallsBackToDefaultsAndStillRefreshes()
    {
        SimpleSwitcherController controller = Controller(new FakeSimpleProviderService(Status(
            current: "openai",
            configured: ["openai"])));
        using SimpleMainForm form = Form(
            controller,
            settingsLoader: _ => Task.FromException<SimpleUserSettings>(
                new InvalidOperationException("settings failed")));

        form.Show();
        System.Windows.Forms.Application.DoEvents();

        Assert.Equal(SimpleActivity.Ready, controller.Snapshot.Activity);
        Assert.Equal("openai", controller.Snapshot.CurrentProviderId);
    }

    [Fact]
    public void Shown_RefreshFailureDoesNotEscapeAsyncVoidAndRendersFailedSnapshot()
    {
        SimpleSwitcherController controller = Controller(new ThrowingStatusProviderService());
        using SimpleMainForm form = Form(controller);

        form.Show();
        System.Windows.Forms.Application.DoEvents();

        Assert.Equal(SimpleActivity.Failed, controller.Snapshot.Activity);
        Assert.Equal("读取状态失败。", StateLabel(form).Text);
    }

    [Fact]
    public void ClipboardFailureIsRenderedAsNonFatalFeedback()
    {
        SimpleSwitcherController controller = Controller(new FakeSimpleProviderService(Status(
            current: "openai",
            configured: ["openai"])));
        using SimpleMainForm form = Form(
            controller,
            clipboardWriter: _ => throw new ExternalException("clipboard busy"));
        form.Show();
        System.Windows.Forms.Application.DoEvents();

        Button copy = Field<Button>(form, "_copyButton");
        copy.PerformClick();

        Assert.Equal("复制失败，请重试。", StateLabel(form).Text);
    }

    [Fact]
    public void SaveFailureDoesNotPreventWindowClosing()
    {
        SimpleSwitcherController controller = Controller(new FakeSimpleProviderService(Status(
            current: "openai",
            configured: ["openai"])));
        using SimpleMainForm form = Form(
            controller,
            settingsSaver: (_, _) => Task.FromException(new InvalidOperationException("save failed")));
        form.Show();
        System.Windows.Forms.Application.DoEvents();

        Exception? error = Record.Exception(form.Close);

        Assert.Null(error);
        Assert.True(form.IsDisposed);
    }

    [Fact]
    public void DisposeWithQueuedSnapshotRenderDoesNotThrow()
    {
        BlockingStatusProviderService service = new(
            Status(current: "openai", configured: ["openai"]),
            Status(current: "custom", configured: ["custom"]));
        SimpleSwitcherController controller = Controller(service);
        SimpleMainForm form = Form(controller);
        form.Show();
        System.Windows.Forms.Application.DoEvents();

        Task refresh = Task.Run(() => controller.RefreshAsync());
        Assert.True(service.WaitForSecondRequest(TimeSpan.FromSeconds(1)));
        form.Dispose();
        Assert.True(service.ReleaseSecondRequest());
        PumpUntil(() => refresh.IsCompleted);
        Assert.True(refresh.IsCompletedSuccessfully);

        Exception? error = Record.Exception(System.Windows.Forms.Application.DoEvents);
        Assert.Null(error);
    }

    private static SimpleSwitcherController Controller(ISimpleProviderService service) => new(
        service,
        new FakeProcessProbe(),
        @"C:\fixture\.codex");

    private static SimpleMainForm Form(
        SimpleSwitcherController controller,
        Func<CancellationToken, Task<SimpleUserSettings>>? settingsLoader = null,
        Func<SimpleUserSettings, CancellationToken, Task>? settingsSaver = null,
        Action<string>? clipboardWriter = null)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "codex-switcher-lifecycle-" + Guid.NewGuid().ToString("N"),
            "settings.json");
        return new SimpleMainForm(
            controller,
            new SimpleSettingsStore(path),
            settingsLoader,
            settingsSaver,
            clipboardWriter);
    }

    private static Label StateLabel(SimpleMainForm form) => Field<Label>(form, "_stateLabel");

    private static T Field<T>(object target, string name) where T : class =>
        Assert.IsType<T>(target.GetType()
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(target));

    private static void PumpUntil(Func<bool> completed)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (!completed() && DateTime.UtcNow < deadline)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Yield();
        }
        Assert.True(completed(), "The asynchronous UI operation did not complete.");
        System.Windows.Forms.Application.DoEvents();
    }

    private sealed class GatedExecuteProviderService : ISimpleProviderService
    {
        private readonly bool _recoveryRequired;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal GatedExecuteProviderService(bool recoveryRequired)
        {
            _recoveryRequired = recoveryRequired;
        }

        public Task<StatusSnapshot> GetStatusAsync(
            string codexHome,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Status(current: "openai", configured: ["openai"]));

        public async Task<SyncResult> ExecuteAsync(
            ApplicationWriteIntent intent,
            CancellationToken cancellationToken = default)
        {
            await _release.Task.WaitAsync(cancellationToken);
            if (_recoveryRequired)
            {
                throw new SimpleApplicationException(
                    ApplicationOperationLifecycle.RecoveryRequired,
                    [new ApplicationError("recovery", "restore required", RecoveryRequired: true)]);
            }
            return new SyncResult
            {
                CodexHome = @"C:\fixture\.codex",
                TargetProvider = "openai",
                PreviousProvider = "openai",
                BackupDir = @"C:\fixture\backup",
                ChangedSessionFiles = 1,
                SkippedLockedRolloutFiles = [],
                SkippedUnreadableRolloutFiles = [],
                SqliteRowsUpdated = 2,
                SqlitePresent = true,
                RolloutCountsBefore = new ProviderCounts(),
                EncryptedContentCounts = new ProviderCounts()
            };
        }

        internal void Release() => _release.TrySetResult();
    }

    private sealed class ThrowingStatusProviderService : ISimpleProviderService
    {
        public Task<StatusSnapshot> GetStatusAsync(
            string codexHome,
            CancellationToken cancellationToken = default) =>
            Task.FromException<StatusSnapshot>(new InvalidOperationException("status failed"));

        public Task<SyncResult> ExecuteAsync(
            ApplicationWriteIntent intent,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
