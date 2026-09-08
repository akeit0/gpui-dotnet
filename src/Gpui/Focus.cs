using Gpui.Interop;

namespace Gpui;

/// <summary>
/// Commands a custom container declared with RenderContext.FocusTarget. Identity survives renders;
/// removing the declaration retires the native target. A CLR handle does not keep it mounted.
/// </summary>
public readonly struct FocusController
{
    private readonly ViewBase? _owner;
    private readonly byte[]? _utf8Key;

    internal FocusController(ViewBase owner, byte[] utf8Key)
    {
        _owner = owner;
        _utf8Key = utf8Key;
    }

    public bool IsBound => _owner is not null;
    internal ViewBase? Owner => _owner;
    internal ReadOnlySpan<byte> Utf8KeySpan => _utf8Key;

    public void Focus() => Dispatch(ResourceCommandKind.FocusTargetFocus);

    /// <summary>Blurs only this target, leaving a focused descendant or another control alone.</summary>
    public void Blur() => Dispatch(ResourceCommandKind.FocusTargetBlur);

    private void Dispatch(ResourceCommandKind command)
    {
        var owner = _owner ?? throw new InvalidOperationException("Default FocusController cannot be used.");
        owner.Runtime.DispatchResourceCommand(new ResourceCommand(ResourceKind.Focus, command,
            null, 0, 0, null, _utf8Key));
    }
}
