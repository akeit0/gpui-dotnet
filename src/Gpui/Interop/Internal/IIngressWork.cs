namespace Gpui.Interop.Internal;

// An existing operation can enter the UI queue without a closure or delegate adapter.
internal interface IIngressWork
{
    void Invoke();
}
