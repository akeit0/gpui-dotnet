using System.Diagnostics;
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Gpui.Interop.Internal.ManagedMetadataUpdateHandler))]

namespace Gpui.Interop.Internal;

/// <summary>Receives standard CLR metadata updates and requests a fresh managed snapshot.</summary>
internal static class ManagedMetadataUpdateHandler
{
    // Invoked by the runtime through MetadataUpdateHandlerAttribute. Keep this callback independent
    // of updatedTypes: helpers and application-owned styles can affect any retained View.
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        _ = updatedTypes;
        foreach (var application in NativeRegistry.Applications.Values)
        {
            try
            {
                application.ApplyManagedCodeUpdate();
            }
            catch (Exception exception)
            {
                // A metadata callback must not escape into the runtime. Debug output is intentionally
                // compiled away in Release and this path is unreachable without an applied update.
                Debug.WriteLine($"GPUI.NET Hot Reload refresh failed: {exception}");
            }
        }
    }
}
