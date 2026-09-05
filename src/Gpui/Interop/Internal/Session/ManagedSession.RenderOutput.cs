namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    private RenderArenaOwner? _rootOutputArena;
    private RenderArenaOwner? _rangeOutputArena;
    private bool _renderOutputActive;

    // Both channels share a guard: a nested callback must not reset either output arena.
    private void EnterRenderOutput(RenderArena* output)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }
        *output = default;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopped) != 0, this);
        if (_renderOutputActive)
        {
            throw new InvalidOperationException("Nested managed render output is not supported.");
        }
        _renderOutputActive = true;
    }

    internal uint RenderRootOutput(RenderArena* output)
    {
        EnterRenderOutput(output);
        try
        {
            var storage = _rootOutputArena ??= new RenderArenaOwner();
            storage.BeginRender();
            var root = RenderRoot(storage.NativeArena);
            storage.PublishTo(output, root);
            return root.Node;
        }
        finally
        {
            _renderOutputActive = false;
        }
    }

    internal uint RenderListRangeOutput(
        ulong rendererToken,
        uint start,
        uint count,
        RenderArena* output
    )
    {
        EnterRenderOutput(output);
        try
        {
            var storage = _rangeOutputArena ??= new RenderArenaOwner();
            storage.BeginRender();
            var root = RenderListRange(rendererToken, start, count, storage.NativeArena);
            storage.PublishTo(output, root);
            return root.Node;
        }
        finally
        {
            _renderOutputActive = false;
        }
    }
}
