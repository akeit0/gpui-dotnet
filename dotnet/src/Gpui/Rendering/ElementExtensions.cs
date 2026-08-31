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
}
