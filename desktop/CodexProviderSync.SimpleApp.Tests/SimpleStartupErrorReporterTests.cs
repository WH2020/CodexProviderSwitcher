using CodexProviderSync.SimpleApp;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class SimpleStartupErrorReporterTests
{
    [Fact]
    public void Report_WritesOriginalErrorAndShowsLogPath()
    {
        SimpleAppPaths paths = Paths();
        Exception original = new InvalidOperationException("composition failed");
        string? dialog = null;

        SimpleStartupErrorReporter.Report(
            original,
            paths,
            WriteLog,
            message => dialog = message);

        Assert.Contains("composition failed", File.ReadAllText(paths.StartupErrorPath));
        Assert.Contains(paths.StartupErrorPath, dialog);
    }

    [Fact]
    public void Report_LogFailurePreservesOriginalErrorAndExplainsLoggingFailure()
    {
        SimpleAppPaths paths = Paths();
        Exception original = new InvalidOperationException("composition failed");
        string? dialog = null;

        Exception? escaped = Record.Exception(() => SimpleStartupErrorReporter.Report(
            original,
            paths,
            (_, _) => throw new UnauthorizedAccessException("log denied"),
            message => dialog = message));

        Assert.Null(escaped);
        Assert.Contains("composition failed", dialog);
        Assert.Contains("日志写入失败", dialog);
    }

    [Fact]
    public void Report_DialogFailureIsTheFinalNonThrowingBoundary()
    {
        SimpleAppPaths paths = Paths();

        Exception? escaped = Record.Exception(() => SimpleStartupErrorReporter.Report(
            new InvalidOperationException("composition failed"),
            paths,
            (_, _) => throw new IOException("disk failed"),
            _ => throw new InvalidOperationException("dialog failed")));

        Assert.Null(escaped);
    }

    private static SimpleAppPaths Paths()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "codex-switcher-startup-" + Guid.NewGuid().ToString("N"));
        return new SimpleAppPaths(
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "startup-error.log"));
    }

    private static void WriteLog(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
