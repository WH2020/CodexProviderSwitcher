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

            string codexHome = new CodexHomeService().NormalizeCodexHome(null);
            SimpleSwitcherController controller = SimpleAppComposition.CreateController(
                codexHome,
                new CodexProcessProbe());
            SimpleSettingsStore settings = new(paths.SettingsPath);
            System.Windows.Forms.Application.Run(new SimpleMainForm(controller, settings));
        }
        catch (Exception error)
        {
            SimpleStartupErrorReporter.Report(error, paths);
        }
    }
}
