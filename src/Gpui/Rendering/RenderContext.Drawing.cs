using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

/// <summary>Controls how overlapping regions are filled when a path crosses itself.</summary>
public enum PathFillRule : uint
{
    NonZero = 0,
    EvenOdd = 1,
}

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Creates a native vector drawing. Paths are painted in argument order and clipped to the
    /// drawing bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DrawingTag> Drawing(params ReadOnlySpan<Element<PathTag>> paths)
    {
        var drawing = ArenaWriter.AddNode<DrawingTag>(_arena, ComponentId.Drawing);
        foreach (ref readonly var path in paths)
        {
            ArenaWriter.AddChild(drawing.Inner, path.Inner);
        }
        return drawing;
    }

    /// <summary>Creates a rectangular path in drawing coordinates.</summary>
    public Element<PathTag> Rect(float x, float y, float width, float height)
    {
        ValidatePoint(x, y);
        ValidatePositiveSize(width, height);
        return Path()
            .MoveTo(x, y)
            .LineTo(x + width, y)
            .LineTo(x + width, y + height)
            .LineTo(x, y + height)
            .Close();
    }

    /// <summary>Creates an elliptical path in drawing coordinates.</summary>
    public Element<PathTag> Ellipse(float centerX, float centerY, float radiusX, float radiusY)
    {
        ValidatePoint(centerX, centerY);
        ValidatePositiveSize(radiusX, radiusY);
        return Path()
            .MoveTo(centerX + radiusX, centerY)
            .ArcTo(radiusX, radiusY, 0, false, true, centerX - radiusX, centerY)
            .ArcTo(radiusX, radiusY, 0, false, true, centerX + radiusX, centerY)
            .Close();
    }

    /// <summary>
    /// Creates a circular path whose center follows drawing coordinates and whose rendered radius
    /// remains uniform when a ViewBox scales its axes independently.
    /// </summary>
    public Element<PathTag> Circle(float centerX, float centerY, float radius)
    {
        ValidatePoint(centerX, centerY);
        ValidatePositiveSize(radius, radius);
        var path = Path();
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathCircleCenter, centerX, centerY);
        ArenaWriter.AddF32(path.Inner, OpCode.PathCircleRadius, radius);
        return path;
    }

    /// <summary>Creates a straight path in drawing coordinates.</summary>
    public Element<PathTag> Line(float startX, float startY, float endX, float endY)
    {
        ValidatePoint(startX, startY);
        ValidatePoint(endX, endY);
        return Path().MoveTo(startX, startY).LineTo(endX, endY);
    }

    private static void ValidatePoint(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Drawing coordinates must be finite.");
        }
    }

    private static void ValidatePositiveSize(float width, float height)
    {
        if (!float.IsFinite(width) || width <= 0 || !float.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Drawing dimensions must be finite and greater than zero."
            );
        }
    }
}

public static partial class ElementExtensions
{
    /// <summary>
    /// Maps drawing coordinates into the element bounds. Mapping is independent on each axis,
    /// which is suitable for responsive plots and charts.
    /// </summary>
    public static Element<DrawingTag> ViewBox(
        this Element<DrawingTag> drawing,
        float x,
        float y,
        float width,
        float height
    )
    {
        ValidatePoint(x, y);
        ValidatePositive(width, nameof(width));
        ValidatePositive(height, nameof(height));
        ArenaWriter.AddF32x2(drawing.Inner, OpCode.DrawingViewBoxOrigin, x, y);
        ArenaWriter.AddF32x2(drawing.Inner, OpCode.DrawingViewBoxSize, width, height);
        return drawing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<PathTag> MoveTo(this Element<PathTag> path, float x, float y)
    {
        ValidatePoint(x, y);
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathMoveTo, x, y);
        return path;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<PathTag> LineTo(this Element<PathTag> path, float x, float y)
    {
        ValidatePoint(x, y);
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathLineTo, x, y);
        return path;
    }

    /// <summary>Adds a quadratic Bézier segment.</summary>
    public static Element<PathTag> QuadraticTo(
        this Element<PathTag> path,
        float controlX,
        float controlY,
        float x,
        float y
    )
    {
        ValidatePoint(controlX, controlY);
        ValidatePoint(x, y);
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathQuadraticControl, controlX, controlY);
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathQuadraticTo, x, y);
        return path;
    }

    /// <summary>Adds a cubic Bézier segment.</summary>
    public static Element<PathTag> CubicTo(
        this Element<PathTag> path,
        float controlAX,
        float controlAY,
        float controlBX,
        float controlBY,
        float x,
        float y
    )
    {
        ValidatePoint(controlAX, controlAY);
        ValidatePoint(controlBX, controlBY);
        ValidatePoint(x, y);
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathCubicControlA, controlAX, controlAY);
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathCubicControlB, controlBX, controlBY);
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathCubicTo, x, y);
        return path;
    }

    /// <summary>Adds an SVG-compatible elliptical arc segment.</summary>
    public static Element<PathTag> ArcTo(
        this Element<PathTag> path,
        float radiusX,
        float radiusY,
        float rotationDegrees,
        bool largeArc,
        bool sweep,
        float x,
        float y
    )
    {
        ValidatePositive(radiusX, nameof(radiusX));
        ValidatePositive(radiusY, nameof(radiusY));
        if (!float.IsFinite(rotationDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotationDegrees),
                "Arc rotation must be finite."
            );
        }
        ValidatePoint(x, y);
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathArcRadii, radiusX, radiusY);
        ArenaWriter.AddF32(path.Inner, OpCode.PathArcRotation, rotationDegrees);
        ArenaWriter.AddU32(
            path.Inner,
            OpCode.PathArcFlags,
            (largeArc ? 1u : 0u) | (sweep ? 2u : 0u)
        );
        ArenaWriter.AddF32x2(path.Inner, OpCode.PathArcTo, x, y);
        return path;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<PathTag> Close(this Element<PathTag> path)
    {
        ArenaWriter.AddNoArg(path.Inner, OpCode.PathClose);
        return path;
    }

    /// <summary>Fills the path using the supplied color and fill rule.</summary>
    public static Element<PathTag> Fill(
        this Element<PathTag> path,
        Color color,
        PathFillRule rule = PathFillRule.NonZero
    )
    {
        if (rule is < PathFillRule.NonZero or > PathFillRule.EvenOdd)
        {
            throw new ArgumentOutOfRangeException(nameof(rule));
        }
        ArenaWriter.AddU32(path.Inner, OpCode.PathFillRgba, color.Rgba);
        ArenaWriter.AddU32(path.Inner, OpCode.PathFillRule, (uint)rule);
        return path;
    }

    /// <summary>Strokes the path using a width measured in device-independent pixels.</summary>
    public static Element<PathTag> Stroke(this Element<PathTag> path, Color color, Pixels width)
    {
        ValidatePositive(width.Value, nameof(width));
        ArenaWriter.AddU32(path.Inner, OpCode.PathStrokeRgba, color.Rgba);
        ArenaWriter.AddF32(path.Inner, OpCode.PathStrokeWidthPx, width.Value);
        return path;
    }

    /// <summary>Applies a repeating on/off dash pattern to the path stroke.</summary>
    public static Element<PathTag> Dash(
        this Element<PathTag> path,
        params ReadOnlySpan<Pixels> pattern
    )
    {
        if (pattern.IsEmpty)
        {
            throw new ArgumentException("A dash pattern cannot be empty.", nameof(pattern));
        }
        foreach (var length in pattern)
        {
            ValidatePositive(length.Value, nameof(pattern));
            ArenaWriter.AddF32(path.Inner, OpCode.PathDashPx, length.Value);
        }
        return path;
    }

    private static void ValidatePoint(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Path coordinates must be finite.");
        }
    }

    private static void ValidatePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The value must be finite and greater than zero."
            );
        }
    }
}
