using CodexProviderSync.Application;
using CodexProviderSync.Core;

namespace CodexProviderSync.SimpleApp;

internal interface ISimpleProviderService
{
    Task<StatusSnapshot> GetStatusAsync(
        string codexHome,
        CancellationToken cancellationToken = default);

    Task<SyncResult> ExecuteAsync(
        ApplicationWriteIntent intent,
        CancellationToken cancellationToken = default);
}

internal sealed class SimpleApplicationException : InvalidOperationException
{
    internal SimpleApplicationException(
        ApplicationOperationLifecycle lifecycle,
        IReadOnlyList<ApplicationError> errors)
        : base(errors.Count == 0
            ? "Application operation ended as " + lifecycle + "."
            : string.Join(Environment.NewLine, errors.Select(item => item.Message)))
    {
        Lifecycle = lifecycle;
        Errors = errors;
    }

    internal ApplicationOperationLifecycle Lifecycle { get; }
    internal IReadOnlyList<ApplicationError> Errors { get; }
    internal bool RecoveryRequired =>
        Lifecycle == ApplicationOperationLifecycle.RecoveryRequired
        || Errors.Any(item => item.RecoveryRequired);
}

internal sealed class SimpleProviderService : ISimpleProviderService
{
    private readonly IApplicationService _application;

    internal SimpleProviderService(IApplicationService application)
    {
        _application = application;
    }

    public async Task<StatusSnapshot> GetStatusAsync(
        string codexHome,
        CancellationToken cancellationToken = default)
    {
        ApplicationOutcome<StatusSnapshot> outcome = await _application.GetStatusAsync(
            new ApplicationStatusRequest(codexHome),
            cancellationToken);
        if (outcome.Lifecycle == ApplicationOperationLifecycle.Succeeded
            && outcome.Data is not null)
        {
            return outcome.Data;
        }
        throw new SimpleApplicationException(outcome.Lifecycle, outcome.Errors);
    }

    public async Task<SyncResult> ExecuteAsync(
        ApplicationWriteIntent intent,
        CancellationToken cancellationToken = default)
    {
        if (intent is not SyncIntent and not SwitchIntent)
        {
            throw new ArgumentOutOfRangeException(nameof(intent));
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            ApplicationOutcome<ApplicationOperationPlan> planned =
                await _application.CreatePlanAsync(
                    new CreateApplicationPlanRequest(intent),
                    cancellationToken);
            ApplicationOperationPlan plan = RequireReadyPlan(planned);
            ApplicationApplyAuthorization authorization = new(
                Apply: true,
                Plan: plan,
                PlanDigest: plan.Digest);
            ApplicationOutcome<ApplicationWriteResult<SyncResult>> applied = intent switch
            {
                SyncIntent sync => await _application.SyncAsync(
                    new SyncApplicationRequest(sync, authorization),
                    cancellationToken),
                SwitchIntent change => await _application.SwitchAsync(
                    new SwitchApplicationRequest(change, authorization),
                    cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(intent))
            };
            if (attempt == 0
                && applied.Errors.Any(item =>
                    string.Equals(item.Code, "plan_stale", StringComparison.Ordinal)))
            {
                continue;
            }
            return RequireAppliedResult(applied);
        }
        throw new InvalidOperationException("The bounded plan retry was exhausted.");
    }

    private static ApplicationOperationPlan RequireReadyPlan(
        ApplicationOutcome<ApplicationOperationPlan> outcome)
    {
        if (outcome.Lifecycle == ApplicationOperationLifecycle.ReadyToApply
            && outcome.Data is not null)
        {
            return outcome.Data;
        }
        throw new SimpleApplicationException(outcome.Lifecycle, outcome.Errors);
    }

    private static SyncResult RequireAppliedResult(
        ApplicationOutcome<ApplicationWriteResult<SyncResult>> outcome)
    {
        if (outcome.Lifecycle == ApplicationOperationLifecycle.Succeeded
            && outcome.Data is { Applied: true, Result: not null })
        {
            return outcome.Data.Result;
        }
        throw new SimpleApplicationException(outcome.Lifecycle, outcome.Errors);
    }
}
