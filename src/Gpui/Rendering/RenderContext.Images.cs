using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Loads an image from a filesystem path through GPUI's native image cache. Relative paths
    /// are resolved against the application's current working directory.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ImageTag> Image(ReadOnlySpan<char> path)
    {
        if (path.IsEmpty)
        {
            throw new ArgumentException("Image path cannot be empty.", nameof(path));
        }

        return ArenaWriter.AddNode<ImageTag>(_arena, ComponentId.Image, path);
    }

    /// <summary>Loads an image from an already UTF-8 encoded filesystem path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ImageTag> Image(ReadOnlySpan<byte> utf8Path)
    {
        if (utf8Path.IsEmpty)
        {
            throw new ArgumentException("Image path cannot be empty.", nameof(utf8Path));
        }

        return ArenaWriter.AddNode<ImageTag>(_arena, ComponentId.Image, utf8Path);
    }
}

public static partial class ElementExtensions
{
    /// <summary>Controls how an image is fitted into its layout bounds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Fit<TTag>(this Element<TTag> element, ImageFit fit)
        where TTag : unmanaged, IImageElementTag
    {
        if (fit is < ImageFit.Fill or > ImageFit.None)
        {
            throw new ArgumentOutOfRangeException(nameof(fit));
        }

        ArenaWriter.AddU32(element.Inner, OpCode.ImageObjectFit, (uint)fit);
        return element;
    }

    /// <summary>Enables or disables native grayscale rendering.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Grayscale<TTag>(this Element<TTag> element, bool enabled = true)
        where TTag : unmanaged, IImageElementTag
    {
        ArenaWriter.AddU32(element.Inner, OpCode.ImageGrayscale, enabled ? 1u : 0u);
        return element;
    }
}
