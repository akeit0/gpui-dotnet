namespace Gpui;

/// <summary>
/// Establishes owned local facilities before the first render. Handles activate after acceptance;
/// construction cannot perform external effects or issue runtime commands.
/// </summary>
public readonly ref struct ViewConstruction
{
    private readonly Gpui.Interop.Internal.ViewOwnership _owner;
    private ViewBase _view => Owner.View;
    private Gpui.Interop.Internal.ViewOwnership Owner
    {
        get
        {
            if (_owner is null)
                throw new InvalidOperationException("A construction context is required.");
            _owner.AssertAccess();
            if (_owner.ConstructionComplete)
                throw new InvalidOperationException("Construction has completed.");
            return _owner;
        }
    }

    internal ViewConstruction(Gpui.Interop.Internal.ViewOwnership owner) => _owner = owner;

    internal Gpui.Interop.Internal.ViewOwnership Bind(ViewBase view)
    {
        var owner = Owner;
        owner.Bind(view);
        return owner;
    }

    public T Own<T>(T resource)
        where T : IDisposable => Owner.Own(resource);

    public Memo<TInput, TResult> Memo<TInput, TResult>()
        where TInput : IEquatable<TInput> => Owner.Memo<TInput, TResult>();

    public Effect<TInput> Effect<TInput>(Action<EffectScope, TInput> setup)
        where TInput : IEquatable<TInput> => Owner.Effect(setup);

    public Dispatcher Dispatcher => _view.Dispatcher;
    public GpuiWindow Window =>
        Owner.Window ?? throw new InvalidOperationException("No window owns this construction.");
    public GpuiApplication Application => Window.Application;

    /// <summary>A stable work handle; starting production requires an accepted View.</summary>
    public WorkScope Work => _view.Runtime.GetConstructionWorkScope();

    /// <summary>
    /// Creates an optional imperative controller for a ui.Scroll() resource owned by this View.
    /// Creating the handle does not declare a resource. Commands require an accepted declaration;
    /// queued commands are revoked when that declaration's presence generation ends.
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

    /// <summary>
    /// Creates an optional imperative controller for a ui.DockArea() resource owned by this View.
    /// Pass it by reference to ui.DockArea() or use the area key it was created with. The same
    /// queued-command lifecycle as <see cref="CreateScrollController"/> applies.
    /// </summary>
    public DockController CreateDockController(string key)
    {
        ValidateResourceKey(key);
        return new DockController(_view, key);
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
