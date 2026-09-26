namespace Gpui.Components;

/// <summary>Toolbar density and native roving-keyboard behavior.</summary>
public sealed record ComponentToolbarOptions
{
    public ComponentSize Size { get; init; } = ComponentSize.Small;
    public bool Disabled { get; init; }
}

/// <summary>Accessible name for a semantic subgroup of toolbar controls.</summary>
public sealed record ComponentToolbarGroupOptions
{
    public string Label { get; init; } = string.Empty;
}

public static partial class ComponentElements
{
    /// <summary>
    /// Declares a native toolbar. Children retain their own sizes and event routes;
    /// the toolbar size sets its density and height.
    /// </summary>
    public static Element<NativeExtensionTag> Toolbar(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentToolbarOptions? options = null,
        params ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.Toolbar,
            key,
            ComponentSchema.Toolbar.EncodeConfiguration(
                (ComponentSchema.Toolbar.Size)(int)options.Size,
                options.Disabled
            ),
            children
        );
    }

    /// <summary>Groups toolbar controls under one accessible label.</summary>
    public static Element<NativeExtensionTag> ToolbarGroup(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentToolbarGroupOptions? options = null,
        params ReadOnlySpan<Element> children
    )
    {
        options ??= new();
        return ui.NativeExtension(
            ComponentsExtension.ToolbarGroup,
            key,
            ComponentSchema.ToolbarGroup.EncodeConfiguration(options.Label),
            children
        );
    }
}
