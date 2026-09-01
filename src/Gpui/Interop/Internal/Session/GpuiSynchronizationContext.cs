namespace Gpui.Interop.Internal.Session;

internal sealed class GpuiSynchronizationContext(ManagedSession session) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        session.Post(() => d(state));
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (!ReferenceEquals(Current, this))
        {
            throw new NotSupportedException("Synchronous cross-thread dispatch is not supported.");
        }
        d(state);
    }

    public override SynchronizationContext CreateCopy() => this;
}
