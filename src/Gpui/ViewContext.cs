namespace Gpui;

/// <summary>
/// Lifecycle/control-plane context. Unlike <see cref="RenderContext"/>, this context is for
/// mounted resources and commands; it is never used to build render IR.
/// </summary>
public readonly ref struct ViewContext
{
    private readonly ViewBase _view;

    internal ViewContext(ViewBase view) => _view = view;

    public Dispatcher Dispatcher => _view.Dispatcher;

    public void Invalidate() => _view.Invalidate();

    /// <summary>
    /// Creates an optional imperative controller for a ui.Scroll() resource owned by this View.
    /// Commands issued before the resource exists are queued, but only while the owning View's
    /// committed snapshots keep declaring the resource: once a snapshot omits it, natively
    /// queued commands for that key are discarded (there is nothing to preserve). Declare the
    /// resource in the same render that first issues commands.
    /// </summary>
    public ScrollController CreateScrollController(string key)
    {
        ValidateResourceKey(key);
        return new ScrollController(_view, key);
    }

    /// <summary>
    /// Creates an optional imperative controller for a ui.List() or ui.Table() resource owned by
    /// this View. Use it for scroll-to-item and structural splice/refresh notifications, which
    /// are queued until the next managed snapshot commits. The same queued-command lifecycle as
    /// <see cref="CreateScrollController"/> applies.
    /// </summary>
    public ListController CreateListController(string key)
    {
        ValidateResourceKey(key);
        return new ListController(_view, key);
    }

    /// <summary>Creates an imperative controller for a ui.Input() resource owned by this View.</summary>
    public InputController CreateInputController(string key)
    {
        ValidateResourceKey(key);
        return new InputController(_view, key);
    }

    /// <summary>Creates an input controller whose retained resource key is already UTF-8.</summary>
    public InputController CreateInputController(ReadOnlySpan<byte> utf8Key)
    {
        ValidateResourceKey(utf8Key);
        return new InputController(_view, utf8Key);
    }

    /// <summary>Creates an imperative controller for a ui.Slider() resource owned by this View.</summary>
    public SliderController CreateSliderController(string key)
    {
        ValidateResourceKey(key);
        return new SliderController(_view, key);
    }

    /// <summary>Creates a slider controller whose retained resource key is already UTF-8.</summary>
    public SliderController CreateSliderController(ReadOnlySpan<byte> utf8Key)
    {
        ValidateResourceKey(utf8Key);
        return new SliderController(_view, utf8Key);
    }

    /// <summary>
    /// Creates an extension-neutral controller for a retained resource. Extension packages should
    /// wrap this in a typed factory and expose only their schema-defined commands.
    /// </summary>
    public NativeExtensionController CreateNativeExtensionController(
        NativeExtensionComponent component,
        string key
    )
    {
        component.Validate(nameof(component));
        ValidateResourceKey(key);
        return new NativeExtensionController(_view, component, key);
    }

    /// <summary>Creates an extension controller whose retained resource key is already UTF-8.</summary>
    public NativeExtensionController CreateNativeExtensionController(
        NativeExtensionComponent component,
        ReadOnlySpan<byte> utf8Key
    )
    {
        component.Validate(nameof(component));
        ValidateResourceKey(utf8Key);
        return new NativeExtensionController(_view, component, utf8Key);
    }

    private static void ValidateResourceKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
        {
            throw new ArgumentException("A native resource key cannot be empty.", nameof(key));
        }
        ResourceKeys.ValidateExplicitChars(key, nameof(key));
    }

    private static void ValidateResourceKey(ReadOnlySpan<byte> utf8Key)
    {
        if (utf8Key.IsEmpty)
        {
            throw new ArgumentException("A native resource key cannot be empty.", nameof(utf8Key));
        }
        if (utf8Key.Contains((byte)0))
        {
            throw new ArgumentException(
                "A native resource key cannot contain NUL.",
                nameof(utf8Key)
            );
        }
        ResourceKeys.ValidateExplicitBytes(utf8Key, nameof(utf8Key));
    }
}
