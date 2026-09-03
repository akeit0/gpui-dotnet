using System.Diagnostics;
using System.Text;
using Gpui.Interop;

namespace Gpui;

/// <summary>Scroll direction for a retained native scroll container.</summary>
public enum ScrollAxis : uint
{
    Vertical = 0,
    Horizontal = 1,
    Both = 2,
}

/// <summary>Native scrolling behavior and presentation.</summary>
public readonly struct ScrollOptions
{
    private readonly bool _initialized;

    public ScrollOptions(
        bool smoothScrolling = true,
        bool showScrollbar = true,
        bool scrollbarGutter = false,
        float scrollbarWidth = 8
    )
    {
        ValidateScrollbarWidth(scrollbarWidth);
        SmoothScrolling = smoothScrolling;
        ShowScrollbar = showScrollbar;
        ScrollbarGutter = scrollbarGutter;
        ScrollbarWidth = scrollbarWidth;
        _initialized = true;
    }

    public bool SmoothScrolling { get; }
    public bool ShowScrollbar { get; }
    public bool ScrollbarGutter { get; }
    public float ScrollbarWidth { get; }

    internal bool EffectiveSmoothScrolling => !_initialized || SmoothScrolling;
    internal bool EffectiveShowScrollbar => !_initialized || ShowScrollbar;
    internal bool EffectiveScrollbarGutter => _initialized && ScrollbarGutter;
    internal float EffectiveScrollbarWidth => _initialized ? ScrollbarWidth : 8;

    internal static void ValidateScrollbarWidth(float width)
    {
        if (!float.IsFinite(width) || width is < 3f or > 32f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ScrollbarWidth),
                "Scrollbar width must be finite and between 3 and 32 px."
            );
        }
    }
}

/// <summary>
/// Optional imperative handle for a scroll resource declared by the same View with ui.Scroll().
/// Most scroll containers do not need a controller.
/// </summary>
[DebuggerDisplay("{DebuggerView,nq}")]
public readonly struct ScrollController
{
    private readonly ViewBase? _owner;
    private readonly byte[]? _utf8Key;

    internal ScrollController(ViewBase owner, string key)
    {
        _owner = owner;
        _utf8Key = Encoding.UTF8.GetBytes(key);
    }

    /// <summary>Internal constructor that takes ownership of an already-encoded key array.</summary>
    internal ScrollController(ViewBase owner, byte[] utf8Key)
    {
        _owner = owner;
        _utf8Key = utf8Key;
    }

    /// <summary>True once this controller has been bound to a resource.</summary>
    public bool IsBound => _utf8Key is not null;

    internal ReadOnlySpan<byte> Utf8KeySpan => _utf8Key;

    public bool IsDefault => _owner is null;

    private string DebuggerView
    {
        get
        {
            if (_utf8Key is null)
            {
                return "unbound";
            }
            return ResourceKeys.TryDecodeAutoKey(_utf8Key, out var id) ? $"auto:{id}" : "explicit";
        }
    }

    /// <summary>Scrolls to a positive content-space offset from the top-left corner.</summary>
    public void ScrollTo(float x, float y)
    {
        if (!float.IsFinite(x) || x < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Scroll position must be finite and non-negative."
            );
        }
        if (!float.IsFinite(y) || y < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(y),
                "Scroll position must be finite and non-negative."
            );
        }
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Scroll,
                ResourceCommandKind.ScrollToOffset,
                null,
                BitConverter.SingleToUInt32Bits(x),
                BitConverter.SingleToUInt32Bits(y),
                Utf8Key: Utf8KeyArray
            )
        );
    }

    public void ScrollToTop() =>
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Scroll,
                ResourceCommandKind.ScrollToTop,
                null,
                0,
                0,
                Utf8Key: Utf8KeyArray
            )
        );

    public void ScrollToBottom() =>
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Scroll,
                ResourceCommandKind.ScrollToBottom,
                null,
                0,
                0,
                Utf8Key: Utf8KeyArray
            )
        );

    private ViewBase Owner =>
        _owner ?? throw new InvalidOperationException("Default ScrollController cannot be used.");
    private byte[] Utf8KeyArray =>
        _utf8Key ?? throw new InvalidOperationException("Default ScrollController cannot be used.");
}
