namespace Gpui;

/// <summary>A top-level menu in an application's native menu bar.</summary>
public sealed class GpuiMenu
{
    /// <summary>Creates a menu with the supplied title and ordered items.</summary>
    public GpuiMenu(string title, params GpuiMenuItem[] items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Any(item => item is null))
        {
            throw new ArgumentException("Menu items cannot contain null entries.", nameof(items));
        }

        Title = title;
        Items = items.ToArray();
    }

    /// <summary>The title shown by the platform menu.</summary>
    public string Title { get; }

    /// <summary>The ordered items in this menu.</summary>
    public IReadOnlyList<GpuiMenuItem> Items { get; }

    internal static void Validate(GpuiMenu menu, string parameterName)
    {
        if (menu.Items.Count == 0)
        {
            return;
        }

        foreach (var item in menu.Items)
        {
            if (item.IsSeparator)
            {
                continue;
            }

            if (item.NestedMenu is not null)
            {
                Validate(item.NestedMenu, parameterName);
            }
            else if (item.Callback is null)
            {
                throw new ArgumentException(
                    $"Menu item '{item.Title}' must have a callback or submenu.",
                    parameterName
                );
            }
        }
    }
}

/// <summary>An action, separator, or submenu in a <see cref="GpuiMenu"/>.</summary>
public sealed class GpuiMenuItem
{
    private readonly Action<ViewBase, ClickEvent>? _eventCallback;

    private GpuiMenuItem(string title, Action? callback, GpuiMenu? submenu, bool separator)
    {
        Title = title;
        Callback = callback;
        NestedMenu = submenu;
        IsSeparator = separator;
        _eventCallback = callback is null ? null : InvokeCallback;
    }

    /// <summary>Creates a menu item that invokes <paramref name="callback"/> when selected.</summary>
    public static GpuiMenuItem Command(string title, Action callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(callback);
        return new GpuiMenuItem(title, callback, null, separator: false);
    }

    /// <summary>Creates a separator between menu items.</summary>
    public static GpuiMenuItem Separator() => new(string.Empty, null, null, separator: true);

    /// <summary>Creates a nested submenu.</summary>
    public static GpuiMenuItem Submenu(GpuiMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return new GpuiMenuItem(menu.Title, null, menu, separator: false);
    }

    /// <summary>The displayed title. Separators have an empty title.</summary>
    public string Title { get; }

    /// <summary>Whether this item is a separator.</summary>
    public bool IsSeparator { get; }

    internal Action? Callback { get; }
    internal GpuiMenu? NestedMenu { get; }

    internal Action<ViewBase, ClickEvent> EventCallback =>
        _eventCallback
        ?? throw new InvalidOperationException("Only command menu items have an event callback.");

    private void InvokeCallback(ViewBase _view, ClickEvent _event) => Callback!.Invoke();
}
