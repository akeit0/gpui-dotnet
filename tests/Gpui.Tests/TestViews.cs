using Gpui;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

internal static class TestViews
{
    // Isolated renderer fixtures allocate their owner explicitly; public factory tests use Spec.
    internal static ViewConstruction Construction() => new(new ViewOwnership());
}

internal sealed class TestCleanup(Action cleanup) : IDisposable
{
    public void Dispose() => cleanup();
}
