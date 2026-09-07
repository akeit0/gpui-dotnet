namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    private RenderArenaOwner? _rootOutputArena;
    private RenderArenaOwner? _demandOutputArena;
    private bool _renderOutputActive;

    // Both channels share a guard: a nested callback must not reset either output arena.
    private void EnterRenderOutput(RenderArena* output)
    {
        Execution.BindThread();
        ThrowIfUnavailable();
        if (Execution.Phase != ExecutionPhase.Idle)
        {
            throw new InvalidOperationException("Nested managed render output is not supported.");
        }
        RequireAcceptedRender();
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }
        *output = default;
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
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally
        {
            _renderOutputActive = false;
        }
    }

    internal uint RenderListRangeOutput(
        ulong rendererToken,
        ulong source,
        uint start,
        uint count,
        RenderArena* output,
        out ulong artifact
    ) => RenderDemandOutput(rendererToken, source, new ListRangeRenderRequest(start, count), output, out artifact);

    internal uint RenderDemandOutput<TRequest>(
        ulong rendererToken,
        ulong source,
        TRequest request,
        RenderArena* output,
        out ulong artifact
    ) where TRequest : struct, IDemandRenderRequest
    {
        artifact = 0;
        EnterRenderOutput(output);
        try
        {
            var storage = _demandOutputArena ??= new RenderArenaOwner();
            storage.BeginRender();
            var root = RenderDemand(rendererToken, source, request, storage.NativeArena, out artifact);
            storage.PublishTo(output, root);
            return root.Node;
        }
        catch (Exception exception)
        {
            RemoveDemandArtifact(artifact);
            artifact = 0;
            RecordFailure(exception);
            throw;
        }
        finally
        {
            _renderOutputActive = false;
        }
    }
}
