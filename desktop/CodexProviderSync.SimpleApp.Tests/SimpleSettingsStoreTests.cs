using CodexProviderSync.SimpleApp;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class SimpleSettingsStoreTests
{
    [Fact]
    public async Task Settings_RoundTripProviderAndWindowBounds()
    {
        string path = NewSettingsPath();
        SimpleSettingsStore store = new(path);
        SimpleUserSettings expected = new(
            "custom",
            new WindowBoundsState
            {
                X = 20,
                Y = 30,
                Width = 560,
                Height = 420,
                Maximized = false
            });

        await store.SaveAsync(expected);

        SimpleUserSettings actual = await store.LoadAsync();
        Assert.Equal("custom", actual.LastProvider);
        Assert.NotNull(actual.WindowBounds);
        Assert.Equal(20, actual.WindowBounds.X);
        Assert.Equal(30, actual.WindowBounds.Y);
        Assert.Equal(560, actual.WindowBounds.Width);
        Assert.Equal(420, actual.WindowBounds.Height);
        Assert.False(actual.WindowBounds.Maximized);
        Assert.Contains("\"lastProvider\"", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("null")]
    public async Task Settings_MissingMalformedOrNull_ReturnsDefaults(string? contents)
    {
        string path = NewSettingsPath();
        if (contents is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
        }

        SimpleUserSettings actual = await new SimpleSettingsStore(path).LoadAsync();

        Assert.Same(SimpleUserSettings.Default, actual);
    }

    [Fact]
    public async Task Save_LeavesUnrelatedTemporaryFilesUntouched()
    {
        string path = NewSettingsPath();
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string unrelated = Path.Combine(directory, ".settings.json.someone-else.tmp");
        await File.WriteAllTextAsync(unrelated, "keep");

        await new SimpleSettingsStore(path).SaveAsync(new SimpleUserSettings("openai", null));

        Assert.True(File.Exists(unrelated));
        Assert.Equal("keep", await File.ReadAllTextAsync(unrelated));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory, ".settings.json.*.tmp"),
            candidate => !string.Equals(candidate, unrelated, StringComparison.Ordinal));
    }

    private static string NewSettingsPath() => Path.Combine(
        Path.GetTempPath(),
        "codex-switcher-settings-" + Guid.NewGuid().ToString("N"),
        "settings.json");
}
