using System.Text.Json;

namespace CodexProviderSync.SimpleApp;

internal sealed class WindowBoundsState
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool Maximized { get; init; }
}

internal sealed record SimpleUserSettings(
    string? LastProvider,
    WindowBoundsState? WindowBounds)
{
    internal static SimpleUserSettings Default { get; } = new(null, null);
}

internal sealed class SimpleSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly string _path;
    private readonly Func<string, CancellationToken, Task<string>> _readAllTextAsync;

    internal SimpleSettingsStore(string path)
        : this(path, File.ReadAllTextAsync)
    {
    }

    internal SimpleSettingsStore(
        string path,
        Func<string, CancellationToken, Task<string>> readAllTextAsync)
    {
        _path = Path.GetFullPath(path);
        _readAllTextAsync = readAllTextAsync
            ?? throw new ArgumentNullException(nameof(readAllTextAsync));
    }

    internal async Task<SimpleUserSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return SimpleUserSettings.Default;
        }
        try
        {
            string json = await _readAllTextAsync(_path, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<SimpleUserSettings>(json, JsonOptions)
                ?? SimpleUserSettings.Default;
        }
        catch (JsonException)
        {
            return SimpleUserSettings.Default;
        }
        catch (IOException)
        {
            return SimpleUserSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return SimpleUserSettings.Default;
        }
    }

    internal async Task SaveAsync(
        SimpleUserSettings settings,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(
            directory,
            "." + Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(temp, json, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
            throw;
        }
    }
}
