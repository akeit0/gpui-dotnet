using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public static partial class ElementExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Width<TTag>(this Element<TTag> element, Length value)
        where TTag : unmanaged, IStyledElementTag
    {
        switch (value.Unit)
        {
            case LengthUnit.Pixels:
                ArenaWriter.AddF32(element.Inner, OpCode.WidthPx, value.Value);
                break;
            case LengthUnit.Percent:
                ArenaWriter.AddF32(element.Inner, OpCode.WidthPercent, value.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }

        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Height<TTag>(this Element<TTag> element, Length value)
        where TTag : unmanaged, IStyledElementTag
    {
        switch (value.Unit)
        {
            case LengthUnit.Pixels:
                ArenaWriter.AddF32(element.Inner, OpCode.HeightPx, value.Value);
                break;
            case LengthUnit.Percent:
                ArenaWriter.AddF32(element.Inner, OpCode.HeightPercent, value.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }

        return element;
    }

    /// <summary>Declares the flex grow factor of an item. Defaults to 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Grow<TTag>(this Element<TTag> element, float factor = 1)
        where TTag : unmanaged, IStyledElementTag
    {
        ArenaWriter.AddF32(element.Inner, OpCode.FlexGrow, factor);
        return element;
    }

    /// <summary>
    /// Declares the stable model identity of a virtualized list row. Declare it on the element
    /// returned by a [GpuiListItem] renderer. Native uses the ID for splice-stable element
    /// identity inside the row, and OnClick events without an explicit payload deliver the ID as
    /// their event payload instead of the positional index. IDs must be unique within one list;
    /// payload 0 is reserved and cannot be used as an ID.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> ItemId<TTag>(this Element<TTag> element, ulong itemId)
        where TTag : unmanaged, IStyledElementTag
    {
        if (itemId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "Item ID 0 is reserved.");
        }

        ArenaWriter.AddU64(element.Inner, OpCode.ListItemId, itemId);
        return element;
    }

    /// <summary>Declares the initial main size of a flex item.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Basis<TTag>(this Element<TTag> element, Length value)
        where TTag : unmanaged, IStyledElementTag
    {
        switch (value.Unit)
        {
            case LengthUnit.Pixels:
                ArenaWriter.AddF32(element.Inner, OpCode.FlexBasisPx, value.Value);
                break;
            case LengthUnit.Percent:
                ArenaWriter.AddF32(element.Inner, OpCode.FlexBasisPercent, value.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }

        return element;
    }

    /// <summary>Declares the flex shrink factor of an item. Must be finite and non-negative.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Shrink<TTag>(this Element<TTag> element, float factor)
        where TTag : unmanaged, IStyledElementTag
    {
        ArenaWriter.AddF32(element.Inner, OpCode.FlexShrink, factor);
        return element;
    }

    /// <summary>Declares the flex wrapping behavior of a container.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Wrap<TTag>(this Element<TTag> element, FlexWrap wrap)
        where TTag : unmanaged, IStyledElementTag
    {
        if ((uint)wrap > (uint)FlexWrap.WrapReverse)
        {
            throw new ArgumentOutOfRangeException(nameof(wrap));
        }

        ArenaWriter.AddU32(element.Inner, OpCode.FlexWrap, (uint)wrap);
        return element;
    }
}
