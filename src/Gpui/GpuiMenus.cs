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
        Items = Array.AsReadOnly(items.ToArray());
    }

    /// <summary>The title shown by the platform menu.</summary>
    public string Title { get; }

    /// <summary>The ordered items in this menu.</summary>
    public IReadOnlyList<GpuiMenuItem> Items { get; }

    internal static void Validate(GpuiMenu menu, string parameterName)
    {
        Validate([menu], parameterName);
    }

    internal static void Validate(IReadOnlyList<GpuiMenu> menus, string parameterName)
    {
        var pending = new Stack<(GpuiMenu Menu, int Depth)>();
        foreach (var menu in menus)
            pending.Push((menu, 1));
        long records = 0;
        long titleBytes = 0;
        while (pending.TryPop(out var entry))
        {
            if (entry.Depth > 32)
                throw new ArgumentException(
                    "Menus support at most 32 nested levels.",
                    parameterName
                );
            records += 1L + entry.Menu.Items.Count;
            titleBytes += System.Text.Encoding.UTF8.GetByteCount(entry.Menu.Title);
            foreach (var item in entry.Menu.Items)
            {
                if (item.NestedMenu is { } nested)
                {
                    records--;
                    pending.Push((nested, entry.Depth + 1));
                }
                else
                    titleBytes += System.Text.Encoding.UTF8.GetByteCount(item.Title);
            }
            if (records > 4096 || titleBytes > 1024 * 1024)
                throw new ArgumentException(
                    "Menus support at most 4096 records and 1 MiB of UTF-8 titles.",
                    parameterName
                );
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
