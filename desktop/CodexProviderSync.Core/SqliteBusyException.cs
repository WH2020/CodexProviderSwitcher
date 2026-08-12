namespace CodexProviderSync.Core;

public sealed class SqliteBusyException : InvalidOperationException
{
    public SqliteBusyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
