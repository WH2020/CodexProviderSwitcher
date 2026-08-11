using CodexProviderSync.Application;
using CodexProviderSync.Core;

namespace CodexProviderSync.SimpleApp;

internal static class SimpleAppComposition
{
    internal static SimpleSwitcherController CreateController(
        string codexHome,
        ICodexProcessProbe processProbe)
    {
        CodexSyncService syncService = new();
        IApplicationService application = new ApplicationService(
            new CoreApplicationStatusPort(syncService),
            new CoreApplicationWritePort(syncService, new CodexHomeService()),
            new InMemoryApplicationPlanLedger());
        return new SimpleSwitcherController(
            new SimpleProviderService(application),
            processProbe,
            codexHome);
    }
}
