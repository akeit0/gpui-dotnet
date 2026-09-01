using System.Reflection;
using System.Reflection.Metadata;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed class HotReloadTests
{
    [Fact]
    public void CoreAssemblyRegistersTheManagedMetadataUpdateHandler()
    {
        var handlers =
            typeof(GpuiApplication).Assembly.GetCustomAttributes<MetadataUpdateHandlerAttribute>();

        Assert.Contains(
            handlers,
            attribute => attribute.HandlerType == typeof(ManagedMetadataUpdateHandler)
        );
    }

    [Fact]
    public void MetadataUpdateWithoutRunningApplicationsIsHarmless()
    {
        ManagedMetadataUpdateHandler.UpdateApplication(null);
        ManagedMetadataUpdateHandler.UpdateApplication([]);
    }
}
