using CodexProviderSync.Application;
using CodexProviderSync.Core;

namespace CodexProviderSync.SimpleApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        SimpleAppPaths paths = SimpleAppPaths.SystemDefault();
        try
        {
            ApplicationConfiguration.Initialize();
            using SimpleInstanceGuard instance = new("Local\\CodexProviderSwitcher.v1");
            if (!instance.IsOwner)
            {
                MessageBox.Show(
                    "Codex Provider Switcher 已经在运行。",
                    "Codex Provider Switcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            CodexSyncService syncService = new();
            IApplicationService application = new ApplicationService(
                new CoreApplicationStatusPort(syncService),
                new CoreApplicationWritePort(syncService, new CodexHomeService()),
                new InMemoryApplicationPlanLedger());
            SimpleProviderService providerService = new(application);
            SimpleSwitcherController controller = new(
                providerService,
                new CodexProcessProbe(),
                new CodexHomeService().NormalizeCodexHome(null));
            SimpleSettingsStore settings = new(paths.SettingsPath);
            System.Windows.Forms.Application.Run(new SimpleMainForm(controller, settings));
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.StartupErrorPath)!);
            File.WriteAllText(paths.StartupErrorPath, error.ToString());
            MessageBox.Show(
                $"Codex Provider Switcher 启动失败。{Environment.NewLine}{Environment.NewLine}" +
                $"详细信息已写入：{Environment.NewLine}{paths.StartupErrorPath}",
                "Codex Provider Switcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
