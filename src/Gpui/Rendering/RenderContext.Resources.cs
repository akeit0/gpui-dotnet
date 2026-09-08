using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint CurrentResourceOwner()
    {
        var handle = OwnerView.Runtime.RuntimeViewHandle;
        return handle != 0
            ? handle
            : throw new InvalidOperationException("The owning View is not mounted.");
    }

    private ViewBase OwnerView =>
        _owner
        ?? throw new InvalidOperationException(
            "Retained native resources require a mounted managed View. Element-only RenderArenaOwner tests cannot create them."
        );

    /// <summary>
    /// Binds an unbound controller to a fresh per-view auto id. Ids are retained inside the
    /// controller field, so this allocates exactly once per field and every later render reuses
    /// the same identity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private InputController BindAutoController(ref InputController controller)
    {
        if (controller.IsBound)
        {
            return controller;
        }
        var owner = OwnerView;
        controller = new InputController(
            owner,
            ResourceKeys.EncodeAutoKey(owner.Runtime.NextResourceKeyId())
        );
        return controller;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ScrollController BindAutoController(ref ScrollController controller)
    {
        if (controller.IsBound)
        {
            return controller;
        }
        var owner = OwnerView;
        controller = new ScrollController(
            owner,
            ResourceKeys.EncodeAutoKey(owner.Runtime.NextResourceKeyId())
        );
        return controller;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ListController BindAutoController(ref ListController controller)
    {
        if (controller.IsBound)
        {
            return controller;
        }
        var owner = OwnerView;
        controller = new ListController(
            owner,
            ResourceKeys.EncodeAutoKey(owner.Runtime.NextResourceKeyId())
        );
        return controller;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SliderController BindAutoController(ref SliderController controller)
    {
        if (controller.IsBound)
        {
            return controller;
        }
        var owner = OwnerView;
        controller = new SliderController(
            owner,
            ResourceKeys.EncodeAutoKey(owner.Runtime.NextResourceKeyId())
        );
        return controller;
    }
}
