using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Writes one opaque extension declaration. Extension packages should wrap this method with a
    /// typed builder and own the configuration format identified by their schema hash.
    /// </summary>
    /// <remarks>
    /// The default native host has no extension providers. Applications using this element must
    /// select a host built with a provider matching <paramref name="component"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<NativeExtensionTag> NativeExtension(
        NativeExtensionComponent component,
        ReadOnlySpan<char> key,
        ReadOnlySpan<char> configuration,
        params ReadOnlySpan<Element> children
    )
    {
        component.Validate(nameof(component));
        if (key.IsEmpty)
        {
            throw new ArgumentException("An extension resource key cannot be empty.", nameof(key));
        }
        ResourceKeys.ValidateExplicitChars(key, nameof(key));
        if (configuration.Contains('\0'))
        {
            throw new ArgumentException(
                "Extension configuration cannot contain NUL characters.",
                nameof(configuration)
            );
        }

        var element = ArenaWriter.AddNativeExtensionNode(
            _arena,
            component,
            key,
            configuration
        );
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddChildren(element.Inner, children);
        return element;
    }
}
