using CodexProviderSync.Core;
using CodexProviderSync.SimpleApp;
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

    private static SimpleSwitcherController Controller(StatusSnapshot status) => new(
        new FakeSimpleProviderService(status),
        new FakeProcessProbe(),
        @"C:\fixture\.codex");
}
