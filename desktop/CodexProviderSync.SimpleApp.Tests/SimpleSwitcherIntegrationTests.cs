using System.Text;
using System.Text.Json;
using CodexProviderSync.Application;
using CodexProviderSync.Core;
using CodexProviderSync.Core.Tests;
using CodexProviderSync.SimpleApp;
using Microsoft.Data.Sqlite;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class SimpleSwitcherIntegrationTests
{
    [Fact]
    public async Task Controller_Refresh_OffersOnlyExplicitlyDeclaredCustomProvider()
    {
        TestCodexHomeFixture fixture = await TestCodexHomeFixture.CreateAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(fixture.CodexHome, "config.toml"),
                "model_provider = \"custom\"\n\n[model_providers.custom]\nbase_url = \"https://example.com\"\n");
            SimpleSwitcherController controller = SimpleAppComposition.CreateController(
                fixture.CodexHome,
                new FakeProcessProbe());

            await controller.RefreshAsync();

            Assert.Equal(["custom"], controller.Snapshot.Providers.Select(item => item.Id));
            Assert.Equal("custom", controller.Snapshot.SelectedProviderId);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Controller_Refresh_AddsImplicitOpenAiToDeclaredCustomProvider()
    {
        TestCodexHomeFixture fixture = await TestCodexHomeFixture.CreateAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(fixture.CodexHome, "config.toml"),
                "[model_providers.custom]\nbase_url = \"https://example.com\"\n");
            SimpleSwitcherController controller = SimpleAppComposition.CreateController(
                fixture.CodexHome,
                new FakeProcessProbe());

            await controller.RefreshAsync();

            Assert.Equal(["openai", "custom"], controller.Snapshot.Providers.Select(item => item.Id));
            Assert.Equal("openai", controller.Snapshot.SelectedProviderId);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Controller_Execute_MapsCoreSqliteBusyThroughApplicationToManualCloseBlock()
    {
        BusyPlanningWritePort writePort = new();
        IApplicationService application = new ApplicationService(
            new FixedStatusPort(SimpleSwitcherTestData.Status("custom", ["custom"])),
            writePort,
            new InMemoryApplicationPlanLedger());
        SimpleSwitcherController controller = new(
            new SimpleProviderService(application),
            new FakeProcessProbe(),
            @"C:\fixture\.codex");
        await controller.RefreshAsync();

        await controller.ExecuteAsync();

        Assert.Equal(SimpleActivity.Blocked, controller.Snapshot.Activity);
        Assert.False(controller.Snapshot.CanExecute);
        Assert.Contains("手动关闭 Codex", controller.Snapshot.Message);
        Assert.Contains("state_5.sqlite is currently in use", controller.Snapshot.Details);
        Assert.Equal(1, writePort.PlanCalls);
        Assert.Equal(0, writePort.ExecuteCalls);
    }

    [Fact]
    public void ReadRootModelProvider_RequiresExactAssignmentKey()
    {
        const string config = """
            model_provider_backup = "apigather"
            model_provider = "openai"

            [model_providers.apigather]
            model_provider = "section-value"
            """;

        Assert.Equal("openai", ReadRootModelProvider(config));
    }

    [Fact]
    public async Task Controller_SwitchesConfigRolloutAndSqliteToConfiguredProvider()
    {
        TestCodexHomeFixture fixture = await TestCodexHomeFixture.CreateAsync();
        try
        {
            AssertFixtureBoundary(fixture);
            await fixture.WriteConfigAsync("model_provider = \"openai\"");
            string rollout = fixture.RolloutPath("sessions", "rollout-a.jsonl");
            await fixture.WriteRolloutAsync(rollout, "thread-a", "openai");
            await fixture.WriteStateDbAsync(
                [("thread-a", "openai", false)],
                model: "gpt-5");

            SimpleSwitcherController controller = SimpleAppComposition.CreateController(
                fixture.CodexHome,
                new FakeProcessProbe());

            await controller.RefreshAsync();
            Assert.True(controller.SelectProvider("apigather"));
            await controller.ExecuteAsync();

            Assert.Equal(SimpleActivity.Success, controller.Snapshot.Activity);
            Assert.Equal("apigather", ReadRootModelProvider(
                await File.ReadAllTextAsync(Path.Combine(fixture.CodexHome, "config.toml"))));
            Assert.Equal("apigather", await ReadRolloutProviderAsync(rollout));
            Assert.Equal("apigather", await ReadSqliteProviderAsync(fixture, "thread-a"));

            string backupRoot = fixture.BackupRoot();
            Assert.True(Directory.Exists(backupRoot));
            Assert.NotEmpty(Directory.EnumerateDirectories(backupRoot));
            Assert.NotNull(controller.Snapshot.LastResult);
            Assert.True(IsWithin(backupRoot, controller.Snapshot.LastResult.BackupDirectory));
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Controller_SameProviderSync_PreservesExactConfigBytes()
    {
        TestCodexHomeFixture fixture = await TestCodexHomeFixture.CreateAsync();
        try
        {
            AssertFixtureBoundary(fixture);
            await fixture.WriteConfigAsync(string.Empty);
            string configPath = Path.Combine(fixture.CodexHome, "config.toml");
            byte[] before = await File.ReadAllBytesAsync(configPath);
            string rollout = fixture.RolloutPath("sessions", "rollout-sync.jsonl");
            await fixture.WriteRolloutAsync(rollout, "thread-sync", "apigather");
            await fixture.WriteStateDbAsync([("thread-sync", "apigather", false)]);

            SimpleSwitcherController controller = SimpleAppComposition.CreateController(
                fixture.CodexHome,
                new FakeProcessProbe());

            await controller.RefreshAsync();
            Assert.True(controller.SelectProvider("openai"));
            await controller.ExecuteAsync();

            Assert.Equal(SimpleActivity.Success, controller.Snapshot.Activity);
            Assert.Equal(before, await File.ReadAllBytesAsync(configPath));
            Assert.Equal("openai", await ReadRolloutProviderAsync(rollout));
            Assert.Equal("openai", await ReadSqliteProviderAsync(fixture, "thread-sync"));
            Assert.NotEmpty(Directory.EnumerateDirectories(fixture.BackupRoot()));
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    private static string? ReadRootModelProvider(string config)
    {
        foreach (string line in config.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                return null;
            }
            int separator = trimmed.IndexOf('=');
            if (separator < 0
                || !string.Equals(
                    trimmed[..separator].Trim(),
                    "model_provider",
                    StringComparison.Ordinal))
            {
                continue;
            }
            return trimmed[(separator + 1)..].Trim().Trim('"');
        }
        return null;
    }

    private static async Task<string?> ReadRolloutProviderAsync(string rolloutPath)
    {
        string firstLine = (await File.ReadAllLinesAsync(rolloutPath, Encoding.UTF8))[0];
        using JsonDocument document = JsonDocument.Parse(firstLine);
        JsonElement root = document.RootElement;
        Assert.Equal("session_meta", root.GetProperty("type").GetString());
        return root.GetProperty("payload").GetProperty("model_provider").GetString();
    }

    private static async Task<string?> ReadSqliteProviderAsync(
        TestCodexHomeFixture fixture,
        string threadId)
    {
        await using Microsoft.Data.Sqlite.SqliteConnection db = fixture.OpenSqliteConnection();
        await db.OpenAsync();
        await using Microsoft.Data.Sqlite.SqliteCommand command = db.CreateCommand();
        command.CommandText = "SELECT model_provider FROM threads WHERE id = $id";
        command.Parameters.AddWithValue("$id", threadId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private static bool IsWithin(string parent, string candidate)
    {
        string relative = Path.GetRelativePath(
            Path.GetFullPath(parent),
            Path.GetFullPath(candidate));
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static void AssertFixtureBoundary(TestCodexHomeFixture fixture)
    {
        Assert.True(IsWithin(Path.GetTempPath(), fixture.Root));
        Assert.True(IsWithin(fixture.Root, fixture.CodexHome));
        Assert.True(IsWithin(fixture.Root, fixture.BackupRoot()));
    }

    private sealed class FixedStatusPort(StatusSnapshot status) : IApplicationStatusPort
    {
        public Task<StatusSnapshot> GetStatusAsync(
            ApplicationStatusRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(status);
    }

    private sealed class BusyPlanningWritePort : IApplicationWritePort
    {
        internal int PlanCalls { get; private set; }
        internal int ExecuteCalls { get; private set; }

        public Task<ApplicationPlanPreview> CreatePlanAsync(
            ApplicationWriteIntent intent,
            string operationId,
            CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            SqliteException original = new("native SQLite busy", 5, 5);
            Exception busy = SqliteStateService.WrapSqliteBusyError(
                original,
                "update session provider metadata");
            return CoreApplicationWritePort.MapCoreFailuresAsync<ApplicationPlanPreview>(
                () => Task.FromException<ApplicationPlanPreview>(busy));
        }

        public Task<SyncResult> ExecuteSyncAsync(
            SyncIntent intent,
            ApplicationOperationPlan plan,
            string operationId,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            throw new InvalidOperationException("Execution must not start when planning is busy.");
        }

        public Task<SyncResult> ExecuteSwitchAsync(SwitchIntent intent, ApplicationOperationPlan plan, string operationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RestoreResult> ExecuteRestoreAsync(RestoreIntent intent, ApplicationOperationPlan plan, string operationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BackupPruneResult> ExecutePruneAsync(PruneIntent intent, ApplicationOperationPlan plan, string operationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
