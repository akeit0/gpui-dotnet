using System.Collections.Concurrent;
using System.Text;
using Gpui.Interop.Internal.Session;

namespace Gpui.Interop.Internal;

/// <summary>
/// Global registries for native interop. Extracted from NativeRuntime to allow
/// ManagedApplication and ManagedSession to be top-level classes (single responsibility).
/// </summary>
internal static class NativeRegistry
{
    internal static readonly ConcurrentDictionary<ulong, ManagedApplication> Applications = new();
    internal static readonly ConcurrentDictionary<ulong, ManagedSession> Sessions = new();

    internal static long NextApplicationId;

    // Retained for future strict UTF-8 validation needs.
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void RecordFailure(ulong sessionId, Exception exception)
    {
        if (Sessions.TryGetValue(sessionId, out var session))
        {
            session.RecordFailure(exception);
        }
    }

    internal static void RecordRenderFailure(ulong sessionId, Exception exception)
    {
        if (Sessions.TryGetValue(sessionId, out var session))
        {
            session.RecordRenderFailure(exception);
        }
    }
}
