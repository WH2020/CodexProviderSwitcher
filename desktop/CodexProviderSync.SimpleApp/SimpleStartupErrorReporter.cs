namespace CodexProviderSync.SimpleApp;

internal static class SimpleStartupErrorReporter
{
    internal static void Report(Exception error, SimpleAppPaths paths) => Report(
        error,
        paths,
        WriteLog,
        message => MessageBox.Show(
            message,
            "Codex Provider Switcher",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error));

    internal static void Report(
        Exception error,
        SimpleAppPaths paths,
        Action<string, string> writeLog,
        Action<string> showDialog)
    {
        string logStatus;
        try
        {
            writeLog(paths.StartupErrorPath, error.ToString());
            logStatus = $"详细信息已写入：{Environment.NewLine}{paths.StartupErrorPath}";
        }
        catch
        {
            logStatus = "启动错误日志写入失败。";
        }

        string message =
            $"Codex Provider Switcher 启动失败。{Environment.NewLine}{Environment.NewLine}" +
            $"{error.Message}{Environment.NewLine}{Environment.NewLine}{logStatus}";
        try
        {
            showDialog(message);
        }
        catch
        {
            // Startup reporting is the final boundary and must never replace the original failure.
        }
    }

    private static void WriteLog(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
