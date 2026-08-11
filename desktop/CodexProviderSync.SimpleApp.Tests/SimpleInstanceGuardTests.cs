using CodexProviderSync.SimpleApp;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class SimpleInstanceGuardTests
{
    [Fact]
    public void SecondInstanceGuardDoesNotOwnTheSameName()
    {
        string name = "Local\\CodexProviderSwitcher.Tests." + Guid.NewGuid().ToString("N");
        using SimpleInstanceGuard first = new(name);
        using SimpleInstanceGuard second = new(name);

        Assert.True(first.IsOwner);
        Assert.False(second.IsOwner);
    }

    [Fact]
    public void DisposingNonOwnerDoesNotReleaseOwnersMutex()
    {
        string name = "Local\\CodexProviderSwitcher.Tests." + Guid.NewGuid().ToString("N");
        using SimpleInstanceGuard first = new(name);
        SimpleInstanceGuard second = new(name);

        second.Dispose();
        using SimpleInstanceGuard third = new(name);

        Assert.True(first.IsOwner);
        Assert.False(third.IsOwner);
    }
}
