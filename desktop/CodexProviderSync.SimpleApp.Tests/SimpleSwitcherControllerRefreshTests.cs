using CodexProviderSync.Core;
using CodexProviderSync.SimpleApp;
using System.Reflection;
using static CodexProviderSync.SimpleApp.Tests.SimpleSwitcherTestData;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class SimpleSwitcherControllerRefreshTests
{
    [Fact]
    public async Task RefreshAsync_OffersOnlyConfiguredProvidersAndImplicitCurrentOpenAi()
    {
        FakeSimpleProviderService service = new(Status(
            current: "openai",
            configured: ["custom"],
            rolloutProviders: ["historical"],
            sqliteSupported: true,
            currentImplicit: true));
        SimpleSwitcherController controller = new(
            service,
            new FakeProcessProbe(),
            @"C:\fixture\.codex");

        await controller.RefreshAsync("historical");

        Assert.Equal(["openai", "custom"], controller.Snapshot.Providers.Select(item => item.Id));
        Assert.Equal("openai", controller.Snapshot.SelectedProviderId);
        Assert.DoesNotContain(controller.Snapshot.Providers, item => item.Id == "historical");
        Assert.True(controller.Snapshot.CanExecute);
    }

    [Fact]
    public async Task RefreshAsync_DisablesWritesForPendingRecovery()
    {
        StatusSnapshot status = Status(
            current: "openai",
            configured: ["openai", "custom"],
            sqliteSupported: true,
            pendingTransactions:
            [
                new TransactionRecoveryInfo(
                    "op-1",
                    "recoveryRequired",
                    @"C:\fixture\backup",
                    @"C:\fixture\journal")
            ]);
        SimpleSwitcherController controller = Controller(status);

        await controller.RefreshAsync();

        Assert.False(controller.Snapshot.CanExecute);
        Assert.Equal(SimpleActivity.RecoveryRequired, controller.Snapshot.Activity);
        Assert.Contains(@"C:\fixture\backup", controller.Snapshot.Details);
    }

    [Fact]
    public async Task RefreshAsync_DisablesWritesForUnsupportedSqlite()
    {
        SimpleSwitcherController controller = Controller(Status(
            current: "openai",
            configured: ["openai"],
            sqliteSupported: false));

        await controller.RefreshAsync();

        Assert.False(controller.Snapshot.CanExecute);
        Assert.Equal(SimpleActivity.Blocked, controller.Snapshot.Activity);
        Assert.Contains("不支持", controller.Snapshot.Message);
    }

    [Fact]
    public async Task SelectProvider_RejectsUnknownProviderAndRecalculatesCanExecute()
    {
        SimpleSwitcherController controller = Controller(Status(
            current: "openai",
            configured: ["openai", "custom"],
            sqliteSupported: true));

        await controller.RefreshAsync();

        Assert.False(controller.SelectProvider("historical"));
        Assert.Equal("openai", controller.Snapshot.SelectedProviderId);
        Assert.True(controller.SelectProvider("custom"));
        Assert.Equal("custom", controller.Snapshot.SelectedProviderId);
        Assert.True(controller.Snapshot.CanExecute);
    }

    [Fact]
    public async Task RefreshAsync_RecoversAfterObserversThrowDuringBothPublications()
    {
        SimpleSwitcherController controller = Controller(Status(
            current: "openai",
            configured: ["openai", "custom"]));
        int notifications = 0;
        controller.SnapshotChanged += (_, _) =>
        {
            notifications++;
            if (notifications <= 2)
            {
                throw new InvalidOperationException("observer failed");
            }
        };

        await controller.RefreshAsync();

        Assert.Equal(SimpleActivity.Ready, controller.Snapshot.Activity);
        Assert.True(controller.Snapshot.CanRefresh);
        Assert.True(controller.Snapshot.CanExecute);

        await controller.RefreshAsync();

        Assert.Equal(SimpleActivity.Ready, controller.Snapshot.Activity);
        Assert.True(controller.Snapshot.CanRefresh);
        Assert.True(controller.Snapshot.CanExecute);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotProbeProcessesWhenRecoveryIsRequired()
    {
        SimpleSwitcherController controller = new(
            new FakeSimpleProviderService(Status(
                current: "openai",
                configured: ["openai"],
                pendingTransactions:
                [new TransactionRecoveryInfo("op-1", "recoveryRequired", @"C:\fixture\backup", @"C:\fixture\journal")])),
            new ThrowingProcessProbe(),
            @"C:\fixture\.codex");

        await controller.RefreshAsync();

        Assert.Equal(SimpleActivity.RecoveryRequired, controller.Snapshot.Activity);
        Assert.Contains(@"C:\fixture\backup", controller.Snapshot.Details);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotProbeProcessesWhenSqliteIsUnsupported()
    {
        SimpleSwitcherController controller = new(
            new FakeSimpleProviderService(Status(
                current: "openai",
                configured: ["openai"],
                sqliteSupported: false)),
            new ThrowingProcessProbe(),
            @"C:\fixture\.codex");

        await controller.RefreshAsync();

        Assert.Equal(SimpleActivity.Blocked, controller.Snapshot.Activity);
        Assert.False(controller.Snapshot.CanExecute);
    }

    [Fact]
    public async Task RefreshAsync_ProvidesAnImmutableProviderList()
    {
        SimpleSwitcherController controller = Controller(Status(
            current: "openai",
            configured: ["openai", "custom"]));

        await controller.RefreshAsync();

        IList<SimpleProviderItem> providers = Assert.IsAssignableFrom<IList<SimpleProviderItem>>(
            controller.Snapshot.Providers);

        Assert.Throws<NotSupportedException>(() => providers[0] = new SimpleProviderItem("changed", false));
    }

    [Fact]
    public async Task RefreshAsync_PrefersAConfiguredPreferredProvider()
    {
        SimpleSwitcherController controller = Controller(Status(
            current: "openai",
            configured: ["openai", "custom"]));

        await controller.RefreshAsync("custom");

        Assert.Equal("custom", controller.Snapshot.SelectedProviderId);
    }

    [Fact]
    public async Task RefreshAsync_FallsBackToFirstConfiguredProviderWhenCurrentIsAbsent()
    {
        SimpleSwitcherController controller = Controller(Status(
            current: "historical",
            configured: ["custom", "azure"]));

        await controller.RefreshAsync();

        Assert.Equal("azure", controller.Snapshot.SelectedProviderId);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotInjectExplicitUnconfiguredOpenAi()
    {
        SimpleSwitcherController controller = Controller(Status(
            current: "openai",
            configured: ["custom"],
            currentImplicit: false));

        await controller.RefreshAsync();

        Assert.Equal(["custom"], controller.Snapshot.Providers.Select(item => item.Id));
    }

    [Fact]
    public async Task SelectProvider_DoesNotPublishRemovedProviderWhenRefreshCompletesDuringValidation()
    {
        BlockingStatusProviderService service = new(
            Status(current: "custom", configured: ["custom"]),
            Status(current: "openai", configured: ["openai"]));
        SimpleSwitcherController controller = new(service, new FakeProcessProbe(), @"C:\fixture\.codex");
        await controller.RefreshAsync();
        List<SimpleSwitcherSnapshot> published = [];
        controller.SnapshotChanged += (_, _) =>
        {
            lock (published)
            {
                published.Add(controller.Snapshot);
            }
        };

        Task? refresh = null;
        bool refreshRequestedWhileSelecting = false;
        bool secondRequestReleased = false;
        TriggeringProviderList providers = new(
            [new SimpleProviderItem("custom", true)],
            () =>
            {
                refresh = Task.Run(() => controller.RefreshAsync());
                refreshRequestedWhileSelecting = service.WaitForSecondRequest(TimeSpan.FromSeconds(1));
                if (refreshRequestedWhileSelecting)
                {
                    secondRequestReleased = service.ReleaseSecondRequest();
                }
            });
        FieldInfo snapshotField = typeof(SimpleSwitcherController).GetField(
            "_snapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        snapshotField.SetValue(controller, controller.Snapshot with { Providers = providers });

        controller.SelectProvider("custom");
        Assert.False(refreshRequestedWhileSelecting);
        Assert.True(service.WaitForSecondRequest(TimeSpan.FromSeconds(1)));
        if (!secondRequestReleased)
        {
            secondRequestReleased = service.ReleaseSecondRequest();
        }
        Assert.True(secondRequestReleased);
        await refresh!;

        Assert.DoesNotContain(controller.Snapshot.Providers, item => item.Id == "custom");
        Assert.NotEqual("custom", controller.Snapshot.SelectedProviderId);
        lock (published)
        {
            Assert.DoesNotContain(published, snapshot =>
                snapshot.SelectedProviderId == "custom" && snapshot.CanExecute);
        }
    }

    [Fact]
    public async Task RefreshAsync_PublishesLoadingWithBothControlsDisabledBeforeServiceCompletes()
    {
        GateStatusProviderService service = new(Status(
            current: "openai",
            configured: ["openai", "custom"]));
        SimpleSwitcherController controller = new(service, new FakeProcessProbe(), @"C:\fixture\.codex");

        Task refresh = controller.RefreshAsync();

        Assert.True(service.WaitForRequest(TimeSpan.FromSeconds(1)));
        Assert.Equal(SimpleActivity.Loading, controller.Snapshot.Activity);
        Assert.False(controller.Snapshot.CanRefresh);
        Assert.False(controller.Snapshot.CanExecute);
        Assert.True(service.Release());
        await refresh;
    }

    [Fact]
    public async Task RefreshAsync_PublishesCompletedControlsTogetherAfterServiceCompletes()
    {
        GateStatusProviderService service = new(Status(
            current: "openai",
            configured: ["openai"]));
        SimpleSwitcherController controller = new(service, new FakeProcessProbe(), @"C:\fixture\.codex");

        Task refresh = controller.RefreshAsync();

        Assert.True(service.WaitForRequest(TimeSpan.FromSeconds(1)));
        Assert.True(service.Release());
        await refresh;

        Assert.Equal(SimpleActivity.Ready, controller.Snapshot.Activity);
        Assert.True(controller.Snapshot.CanRefresh);
        Assert.True(controller.Snapshot.CanExecute);
    }

    private static SimpleSwitcherController Controller(StatusSnapshot status) => new(
        new FakeSimpleProviderService(status),
        new FakeProcessProbe(),
        @"C:\fixture\.codex");
}
