using System.Reflection;

namespace Gpui.Tests;

public sealed class EventContractTests
{
    [Fact]
    public void PublicEventBindingsAcceptOnlySynchronousCallbacks()
    {
        var callbacks = new[] { typeof(ElementExtensions), typeof(RenderContext) }
            .SelectMany(type =>
                type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            )
            .Where(method =>
                method.Name.StartsWith("On", StringComparison.Ordinal)
                || method.Name == "BindNativeExtensionEvent"
            )
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.Name == "callback")
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.True(callbacks.Length > 20);
        Assert.All(
            callbacks,
            callback => Assert.Equal(typeof(void), callback.GetMethod("Invoke")!.ReturnType)
        );
    }
}
