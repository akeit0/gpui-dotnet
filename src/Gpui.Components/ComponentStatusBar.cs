namespace Gpui.Components;

public static partial class ComponentElements
{
    /// <summary>
    /// Declares a native status bar with independent left, center, and right regions.
    /// Wrap multiple items for one region in a managed layout container.
    /// </summary>
    public static Element<NativeExtensionTag> StatusBar(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        Element? left = null,
        Element? center = null,
        Element? right = null
    ) =>
        ui.NativeExtension(
            ComponentsExtension.StatusBar,
            key,
            ComponentSchema.StatusBar.EncodeConfiguration(
                left.HasValue,
                center.HasValue,
                right.HasValue
            ),
            ComponentSlots.Pack(left, center, right)
        );
}
