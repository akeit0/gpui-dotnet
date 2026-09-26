using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Makes an existing Div container a retained native focus target without adding a wrapper.
    /// Tab participation is opt-in; pointer focus and the theme's keyboard focus ring are native.
    /// Store the controller in the owning View and bind it to exactly one container per render.
    /// </summary>
    public Element<DivTag> FocusTarget(
        ref FocusController controller,
        Element<DivTag> element,
        bool tabStop = false
    )
    {
        using var access = Access();
        ManagedValidator.ValidateRoot(access.Arena, element.Inner);
        var owner = OwnerView;
        if (controller.IsBound && !ReferenceEquals(controller.Owner, owner))
            throw new InvalidOperationException(
                "A focus controller must be declared by its owning View."
            );
        if (!controller.IsBound)
            controller = new FocusController(
                owner,
                ResourceKeys.EncodeAutoKey(owner.Runtime.NextResourceKeyId())
            );
        ArenaWriter.AddData(element.Inner, OpCode.FocusTarget, controller.Utf8KeySpan);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.FocusTabStop, tabStop ? 1u : 0u);
        return element;
    }
}
