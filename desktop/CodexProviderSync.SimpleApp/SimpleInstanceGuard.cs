namespace CodexProviderSync.SimpleApp;

internal sealed class SimpleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    internal SimpleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        IsOwner = createdNew;
    }

    internal bool IsOwner { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            if (IsOwner)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (ApplicationException)
        {
        }
        finally
        {
            _mutex.Dispose();
        }
    }
}
