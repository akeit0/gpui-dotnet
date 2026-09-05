using System.Reflection;

namespace Gpui.Tests;

public sealed class EventContractTests
{
    [Fact]
    public void PublicEventBindingsAcceptOnlySynchronousCallbacks()
    {
        var callbacks = new[] { typeof(ElementExtensions), typeof(RenderContext) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(method => method.Name.StartsWith("On", StringComparison.Ordinal)
                || method.Name == "BindNativeExtensionEvent")
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.Name == "callback")
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.True(callbacks.Length > 20);
        Assert.All(callbacks, callback => Assert.Equal(typeof(void), callback.GetMethod("Invoke")!.ReturnType));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsyncVoidDelegatesAreRejectedBeforeAnyHandlerRuns(bool multicast)
    {
        var view = new EventView();
        view.PrepareRuntime(1, static callback => callback(), static _ => { }, static (_, _) => { },
            static (_, _, _) => { }, static (_, _, _, _, _, _, _, _, _, _) => { }, static () => { });
        view.MountRuntime();
        try
        {
            var ran = false;
            Action<EventView, ClickEvent> callback = async (_, _) =>
            {
                ran = true;
                await Task.Yield();
            };
            if (multicast)
                callback += (_, _) => ran = true;
            var error = Assert.Throws<InvalidOperationException>(() => view.BindClick(callback));
            Assert.Contains("synchronous", error.Message);
            Assert.False(ran);
            // Rejection must not consume binding identity or corrupt the event registry.
            var token = view.BindClick<EventView>((_, _) => ran = true);
            Assert.Equal(0x8000_0001u, unchecked((uint)token));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    private sealed class EventView : View
    {
        protected override Element Render(ref RenderContext ui) => ui.Div();
    }
}
