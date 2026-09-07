namespace Gpui.Interop.Internal.Session;

// Adapters own request validation and output shape. The session owns publication,
// render purity, ambient theme, dependencies, events, and artifact lifetime.
internal interface IDemandRenderRequest
{
    void Validate();
    Element Render(ViewBase owner, uint rendererId, ref RenderContext ui);
}

internal readonly struct ListRangeRenderRequest(uint start, uint count) : IDemandRenderRequest
{
    public void Validate()
    {
        if (count is 0 or > 512 || (ulong)start + count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(count));
    }

    public Element Render(ViewBase owner, uint rendererId, ref RenderContext ui)
    {
        var root = ui.Div();
        for (uint offset = 0; offset < count; offset++)
            ArenaWriter.AddChild(root, owner.RenderListItemCore(rendererId, checked((int)(start + offset)), ref ui));
        return root;
    }
}
