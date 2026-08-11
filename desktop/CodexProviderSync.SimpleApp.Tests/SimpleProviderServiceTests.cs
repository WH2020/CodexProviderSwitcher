using CodexProviderSync.Application;
using CodexProviderSync.Core;
using CodexProviderSync.SimpleApp;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class SimpleProviderServiceTests
{
    [Fact]
    public async Task GetStatusAsync_ReturnsSucceededStatus()
    {
        FakeApplicationService application = FakeApplicationService.StatusSuccess();
        SimpleProviderService service = new(application);

        StatusSnapshot status = await service.GetStatusAsync(@"C:\fixture\.codex", CancellationToken.None);

        Assert.Equal(@"C:\fixture\.codex", status.CodexHome);
        Assert.Equal(@"C:\fixture\.codex", Assert.Single(application.StatusRequests).CodexHome);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesTheExactPlanAndRoutesSwitchIntent()
    {
        FakeApplicationService application = FakeApplicationService.SwitchSuccess();
        SimpleProviderService service = new(application);
        SwitchIntent intent = new(
            @"C:\fixture\.codex",
            null,
            "custom",
            new FollowProviderModelSelection(),
            AppConstants.DefaultBackupRetentionCount);

        SyncResult result = await service.ExecuteAsync(intent, CancellationToken.None);

        Assert.Equal("custom", result.TargetProvider);
        Assert.Single(application.CreatedPlans);
        SwitchApplicationRequest request = Assert.Single(application.SwitchRequests);
        Assert.True(request.Authorization!.Apply);
        Assert.Same(application.CreatedPlans[0], request.Authorization.Plan);
        Assert.Equal(application.CreatedPlans[0].Digest, request.Authorization.PlanDigest);
    }

    [Fact]
    public async Task ExecuteAsync_RoutesSyncIntent()
    {
        FakeApplicationService application = FakeApplicationService.SyncSuccess();
        SimpleProviderService service = new(application);

        SyncResult result = await service.ExecuteAsync(
            new SyncIntent(@"C:\fixture\.codex", null, "openai"),
            CancellationToken.None);

        Assert.Equal("openai", result.TargetProvider);
        Assert.Single(application.SyncRequests);
        Assert.Empty(application.SwitchRequests);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesPlanStaleExactlyOnce()
    {
        FakeApplicationService application = FakeApplicationService.PlanStaleThenSyncSuccess();
        SimpleProviderService service = new(application);

        await service.ExecuteAsync(
            new SyncIntent(@"C:\fixture\.codex", null, "openai"),
            CancellationToken.None);

        Assert.Equal(2, application.CreatedPlans.Count);
        Assert.Equal(2, application.SyncRequests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesRecoveryEvidence()
    {
        FakeApplicationService application = FakeApplicationService.RecoveryRequired(
            "rollback_failed",
            @"C:\fixture\.codex\backups_state\provider-sync\bound");
        SimpleProviderService service = new(application);

        SimpleApplicationException error = await Assert.ThrowsAsync<SimpleApplicationException>(
            () => service.ExecuteAsync(
                new SyncIntent(@"C:\fixture\.codex", null, "openai"),
                CancellationToken.None));

        Assert.True(error.RecoveryRequired);
        Assert.Contains(error.Errors, item => item.EvidencePath!.EndsWith("bound"));
    }

    private sealed class FakeApplicationService : IApplicationService
    {
        private readonly Queue<ApplicationOutcome<ApplicationOperationPlan>> _plans = new();
        private readonly Queue<ApplicationOutcome<ApplicationWriteResult<SyncResult>>> _syncOutcomes = new();
        private readonly Queue<ApplicationOutcome<ApplicationWriteResult<SyncResult>>> _switchOutcomes = new();
        private readonly Queue<ApplicationOutcome<StatusSnapshot>> _statusOutcomes = new();

        public List<ApplicationStatusRequest> StatusRequests { get; } = [];
        public List<ApplicationOperationPlan> CreatedPlans { get; } = [];
        public List<SyncApplicationRequest> SyncRequests { get; } = [];
        public List<SwitchApplicationRequest> SwitchRequests { get; } = [];

        public static FakeApplicationService StatusSuccess()
        {
            FakeApplicationService service = new();
            service._statusOutcomes.Enqueue(SucceededStatus());
            return service;
        }

        public static FakeApplicationService SyncSuccess()
        {
            FakeApplicationService service = new();
            ApplicationOperationPlan plan = CreatePlan("sync-plan");
            service._plans.Enqueue(ReadyPlan(plan));
            service._syncOutcomes.Enqueue(SucceededSync(plan, "openai"));
            return service;
        }

        public static FakeApplicationService SwitchSuccess()
        {
            FakeApplicationService service = new();
            ApplicationOperationPlan plan = CreatePlan("switch-plan");
            service._plans.Enqueue(ReadyPlan(plan));
            service._switchOutcomes.Enqueue(SucceededSync(plan, "custom"));
            return service;
        }

        public static FakeApplicationService PlanStaleThenSyncSuccess()
        {
            FakeApplicationService service = new();
            ApplicationOperationPlan stalePlan = CreatePlan("stale-plan");
            ApplicationOperationPlan freshPlan = CreatePlan("fresh-plan");
            service._plans.Enqueue(ReadyPlan(stalePlan));
            service._plans.Enqueue(ReadyPlan(freshPlan));
            service._syncOutcomes.Enqueue(Outcome<ApplicationWriteResult<SyncResult>>(
                ApplicationOperationKind.Sync,
                ApplicationOperationLifecycle.Rejected,
                null,
                [new ApplicationError("plan_stale", "The plan is stale.")]));
            service._syncOutcomes.Enqueue(SucceededSync(freshPlan, "openai"));
            return service;
        }

        public static FakeApplicationService RecoveryRequired(string code, string evidencePath)
        {
            FakeApplicationService service = new();
            service._plans.Enqueue(Outcome<ApplicationOperationPlan>(
                ApplicationOperationKind.Plan,
                ApplicationOperationLifecycle.RecoveryRequired,
                null,
                [new ApplicationError(code, "Recovery is required.", true, EvidencePath: evidencePath)]));
            return service;
        }

        public Task<ApplicationOutcome<ApplicationDescription>> DescribeAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ApplicationOutcome<StatusSnapshot>> GetStatusAsync(ApplicationStatusRequest request, CancellationToken cancellationToken = default)
        {
            StatusRequests.Add(request);
            return Task.FromResult(_statusOutcomes.Dequeue());
        }

        public Task<ApplicationOutcome<ApplicationOperationPlan>> CreatePlanAsync(CreateApplicationPlanRequest request, CancellationToken cancellationToken = default)
        {
            ApplicationOutcome<ApplicationOperationPlan> outcome = _plans.Dequeue();
            if (outcome.Data is not null)
            {
                CreatedPlans.Add(outcome.Data);
            }
            return Task.FromResult(outcome);
        }

        public Task<ApplicationOutcome<ApplicationWriteResult<SyncResult>>> SyncAsync(SyncApplicationRequest request, CancellationToken cancellationToken = default)
        {
            SyncRequests.Add(request);
            return Task.FromResult(_syncOutcomes.Dequeue());
        }

        public Task<ApplicationOutcome<ApplicationWriteResult<SyncResult>>> SwitchAsync(SwitchApplicationRequest request, CancellationToken cancellationToken = default)
        {
            SwitchRequests.Add(request);
            return Task.FromResult(_switchOutcomes.Dequeue());
        }

        public Task<ApplicationOutcome<ApplicationWriteResult<RestoreResult>>> RestoreAsync(RestoreApplicationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ApplicationOutcome<ApplicationWriteResult<BackupPruneResult>>> PruneAsync(PruneApplicationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static ApplicationOutcome<StatusSnapshot> SucceededStatus() =>
            Outcome(
                ApplicationOperationKind.Status,
                ApplicationOperationLifecycle.Succeeded,
                new StatusSnapshot
                {
                    CodexHome = @"C:\fixture\.codex",
                    CurrentProvider = new CurrentProviderInfo("openai", false),
                    ConfiguredProviders = ["openai"],
                    RolloutCounts = new ProviderCounts(),
                    LockedRolloutFiles = [],
                    UnreadableRolloutFiles = [],
                    EncryptedContentCounts = new ProviderCounts(),
                    SqliteCounts = null,
                    BackupRoot = @"C:\fixture\.codex\backups_state\provider-sync",
                    BackupSummary = new BackupSummary { Count = 0, TotalBytes = 0 }
                });

        private static ApplicationOperationPlan CreatePlan(string planId) => new(
            ApplicationProtocol.Version,
            planId,
            "operation-" + planId,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(5),
            new SyncIntent(@"C:\fixture\.codex", null, "openai"),
            "state-" + planId,
            "token-" + planId,
            [], [], [], "digest-" + planId);

        private static ApplicationOutcome<ApplicationOperationPlan> ReadyPlan(ApplicationOperationPlan plan) =>
            Outcome(ApplicationOperationKind.Plan, ApplicationOperationLifecycle.ReadyToApply, plan);

        private static ApplicationOutcome<ApplicationWriteResult<SyncResult>> SucceededSync(ApplicationOperationPlan plan, string provider) =>
            Outcome(
                ApplicationOperationKind.Sync,
                ApplicationOperationLifecycle.Succeeded,
                new ApplicationWriteResult<SyncResult>(plan, true, new SyncResult
                {
                    CodexHome = @"C:\fixture\.codex",
                    TargetProvider = provider,
                    PreviousProvider = "openai",
                    BackupDir = @"C:\fixture\.codex\backups_state\provider-sync\bound",
                    ChangedSessionFiles = 0,
                    SkippedLockedRolloutFiles = [],
                    SkippedUnreadableRolloutFiles = [],
                    SqliteRowsUpdated = 0,
                    SqlitePresent = false,
                    RolloutCountsBefore = new ProviderCounts(),
                    EncryptedContentCounts = new ProviderCounts()
                }));

        private static ApplicationOutcome<T> Outcome<T>(
            ApplicationOperationKind operation,
            ApplicationOperationLifecycle lifecycle,
            T? data,
            IReadOnlyList<ApplicationError>? errors = null)
            where T : class => new(
                "operation-" + operation,
                operation,
                lifecycle,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                data,
                [],
                errors ?? [],
                []);
    }
}
