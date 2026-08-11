namespace CodexProviderSync.SimpleApp;

internal sealed record SimpleAppPaths(
    string SettingsPath,
    string StartupErrorPath)
{
    internal static SimpleAppPaths SystemDefault()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "codex-provider-switcher");
        return new(
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "startup-error.log"));
    }
}
