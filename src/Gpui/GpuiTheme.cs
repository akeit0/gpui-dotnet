using System.Text.Json;

namespace Gpui;

/// <summary>Controls the light or dark defaults used by a <see cref="GpuiTheme"/>.</summary>
public enum GpuiThemeAppearance
{
    Light,
    Dark,
}

/// <summary>Shared font-size tokens for application and component typography.</summary>
public sealed class GpuiThemeTypography
{
    public float Display { get; init; } = 28;
    public float Large { get; init; } = 24;
    public float Heading { get; init; } = 18;
    public float Title { get; init; } = 16;
    public float Body { get; init; } = 14;
    public float BodySmall { get; init; } = 13;
    public float Detail { get; init; } = 12;
    public float Caption { get; init; } = 11;
    public float Metric { get; init; } = 28;
    public float Button { get; init; } = 12;
    public float TitleBar { get; init; } = 13;

    internal static GpuiThemeTypography CreateDefault(GpuiThemeAppearance appearance) =>
        appearance == GpuiThemeAppearance.Dark
            ? new GpuiThemeTypography
            {
                Display = 28,
                Large = 24,
                Heading = 18,
                Title = 16,
                Body = 14,
                BodySmall = 13,
                Detail = 12,
                Caption = 11,
                Metric = 28,
                Button = 12,
                TitleBar = 13,
            }
            : new GpuiThemeTypography();

    internal static GpuiThemeTypography FromJson(JsonElement source, GpuiThemeAppearance appearance)
    {
        var defaults = CreateDefault(appearance);
        return new GpuiThemeTypography
        {
            Display = ReadSize(source, defaults.Display, "display"),
            Large = ReadSize(source, defaults.Large, "large"),
            Heading = ReadSize(source, defaults.Heading, "heading"),
            Title = ReadSize(source, defaults.Title, "title"),
            Body = ReadSize(source, defaults.Body, "body"),
            BodySmall = ReadSize(source, defaults.BodySmall, "body_small"),
            Detail = ReadSize(source, defaults.Detail, "detail"),
            Caption = ReadSize(source, defaults.Caption, "caption"),
            Metric = ReadSize(source, defaults.Metric, "metric"),
            Button = ReadSize(source, defaults.Button, "button"),
            TitleBar = ReadSize(source, defaults.TitleBar, "title_bar"),
        };
    }

    private static float ReadSize(
        JsonElement source,
        float fallback,
        params ReadOnlySpan<string> names
    )
    {
        foreach (var property in source.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (
                    !property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && !property.Name.Equals(
                        name.Replace('_', '-'),
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !property.Name.Equals(ToCamelCase(name), StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                if (
                    property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetSingle(out var value)
                    || !float.IsFinite(value)
                    || value <= 0
                )
                {
                    throw new JsonException(
                        $"Theme typography '{property.Name}' must be a positive number."
                    );
                }
                return value;
            }
        }
        return fallback;
    }

    private static string ToCamelCase(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        var upper = false;
        foreach (var character in value)
        {
            if (character is '_' or '-')
            {
                upper = true;
                continue;
            }
            result.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }
        return result.ToString();
    }
}

/// <summary>
/// Semantic colors shared by GPUI.NET components. The names follow the core color vocabulary
/// used by GPUI Component and Zed, including dotted aliases accepted by JSON theme files.
/// </summary>
public sealed class GpuiThemeColors
{
    public Color Border { get; init; } = Colors.Hex("#CBD5E1");
    public Color BorderVariant { get; init; } = Colors.Hex("#E2E8F0");
    public Color BorderFocused { get; init; } = Colors.Hex("#818CF8");
    public Color BorderSelected { get; init; } = Colors.Hex("#6366F1");
    public Color BorderTransparent { get; init; } = Colors.Rgba(0, 0, 0, 0);
    public Color BorderDisabled { get; init; } = Colors.Hex("#CBD5E1");

    public Color ElevatedSurfaceBackground { get; init; } = Colors.Hex("#FFFFFF");
    public Color SurfaceBackground { get; init; } = Colors.Hex("#FFFFFF");
    public Color Background { get; init; } = Colors.Hex("#F8FAFC");
    public Color ElementBackground { get; init; } = Colors.Hex("#FFFFFF");
    public Color ElementHover { get; init; } = Colors.Hex("#F1F5F9");
    public Color ElementActive { get; init; } = Colors.Hex("#E2E8F0");
    public Color ElementSelected { get; init; } = Colors.Hex("#E0E7FF");
    public Color ElementDisabled { get; init; } = Colors.Hex("#F1F5F9");
    public Color GhostElementBackground { get; init; } = Colors.Rgba(0, 0, 0, 0);
    public Color GhostElementHover { get; init; } = Colors.Hex("#F1F5F9");
    public Color GhostElementActive { get; init; } = Colors.Hex("#E2E8F0");
    public Color GhostElementSelected { get; init; } = Colors.Hex("#E0E7FF");
    public Color GhostElementDisabled { get; init; } = Colors.Rgba(0, 0, 0, 0);

    public Color Text { get; init; } = Colors.Hex("#0F172A");
    public Color TextMuted { get; init; } = Colors.Hex("#64748B");
    public Color TextPlaceholder { get; init; } = Colors.Hex("#94A3B8");
    public Color TextDisabled { get; init; } = Colors.Hex("#94A3B8");
    public Color TextAccent { get; init; } = Colors.Hex("#4338CA");
    public Color TextOnAccent { get; init; } = Colors.Hex("#FFFFFF");
    public Color Accent { get; init; } = Colors.Hex("#4F46E5");
    public Color AccentHover { get; init; } = Colors.Hex("#4338CA");
    public Color AccentActive { get; init; } = Colors.Hex("#3730A3");
    public Color Icon { get; init; } = Colors.Hex("#334155");
    public Color IconMuted { get; init; } = Colors.Hex("#64748B");
    public Color IconDisabled { get; init; } = Colors.Hex("#94A3B8");
    public Color IconPlaceholder { get; init; } = Colors.Hex("#94A3B8");
    public Color IconAccent { get; init; } = Colors.Hex("#4F46E5");

    public Color StatusBarBackground { get; init; } = Colors.Hex("#F1F5F9");
    public Color TitleBarBackground { get; init; } = Colors.Hex("#0F172A");
    public Color TitleBarInactiveBackground { get; init; } = Colors.Hex("#1E293B");
    public Color TitleBarText { get; init; } = Colors.Hex("#F8FAFC");
    public Color TitleBarHover { get; init; } = Colors.Hex("#1E293B");
    public Color TitleBarCloseHover { get; init; } = Colors.Hex("#E81120");
    public Color ToolbarBackground { get; init; } = Colors.Hex("#FFFFFF");
    public Color TabBarBackground { get; init; } = Colors.Hex("#F1F5F9");
    public Color TabInactiveBackground { get; init; } = Colors.Hex("#F1F5F9");
    public Color TabActiveBackground { get; init; } = Colors.Hex("#FFFFFF");
    public Color PanelBackground { get; init; } = Colors.Hex("#FFFFFF");
    public Color PanelFocusedBorder { get; init; } = Colors.Hex("#818CF8");

    public Color ScrollbarThumbBackground { get; init; } = Colors.Hex("#94A3B8");
    public Color ScrollbarThumbHoverBackground { get; init; } = Colors.Hex("#64748B");
    public Color ScrollbarThumbActiveBackground { get; init; } = Colors.Hex("#475569");
    public Color ScrollbarThumbBorder { get; init; } = Colors.Hex("#CBD5E1");
    public Color ScrollbarTrackBackground { get; init; } = Colors.Rgba(0, 0, 0, 0);
    public Color ScrollbarTrackBorder { get; init; } = Colors.Hex("#E2E8F0");

    public Color Success { get; init; } = Colors.Hex("#15803D");
    public Color SuccessBackground { get; init; } = Colors.Hex("#DCFCE7");
    public Color Warning { get; init; } = Colors.Hex("#B45309");
    public Color WarningBackground { get; init; } = Colors.Hex("#FEF3C7");
    public Color Error { get; init; } = Colors.Hex("#B91C1C");
    public Color ErrorBackground { get; init; } = Colors.Hex("#FEE2E2");
    public Color Info { get; init; } = Colors.Hex("#1D4ED8");
    public Color InfoBackground { get; init; } = Colors.Hex("#DBEAFE");
    public Color LinkTextHover { get; init; } = Colors.Hex("#4338CA");

    internal static GpuiThemeColors CreateDefault(GpuiThemeAppearance appearance) =>
        appearance == GpuiThemeAppearance.Dark
            ? new GpuiThemeColors
            {
                Border = Colors.Hex("#475569"),
                BorderVariant = Colors.Hex("#334155"),
                BorderFocused = Colors.Hex("#818CF8"),
                BorderSelected = Colors.Hex("#6366F1"),
                BorderDisabled = Colors.Hex("#334155"),
                ElevatedSurfaceBackground = Colors.Hex("#1E293B"),
                SurfaceBackground = Colors.Hex("#1E293B"),
                Background = Colors.Hex("#0F172A"),
                ElementBackground = Colors.Hex("#1E293B"),
                ElementHover = Colors.Hex("#334155"),
                ElementActive = Colors.Hex("#475569"),
                ElementSelected = Colors.Hex("#3730A3"),
                ElementDisabled = Colors.Hex("#1E293B"),
                GhostElementHover = Colors.Hex("#334155"),
                GhostElementActive = Colors.Hex("#475569"),
                GhostElementSelected = Colors.Hex("#3730A3"),
                Text = Colors.Hex("#F8FAFC"),
                TextMuted = Colors.Hex("#94A3B8"),
                TextPlaceholder = Colors.Hex("#64748B"),
                TextDisabled = Colors.Hex("#64748B"),
                TextAccent = Colors.Hex("#A5B4FC"),
                TextOnAccent = Colors.Hex("#0F172A"),
                Accent = Colors.Hex("#818CF8"),
                AccentHover = Colors.Hex("#A5B4FC"),
                AccentActive = Colors.Hex("#6366F1"),
                Icon = Colors.Hex("#E2E8F0"),
                IconMuted = Colors.Hex("#94A3B8"),
                IconDisabled = Colors.Hex("#64748B"),
                IconPlaceholder = Colors.Hex("#94A3B8"),
                IconAccent = Colors.Hex("#A5B4FC"),
                StatusBarBackground = Colors.Hex("#1E293B"),
                TitleBarBackground = Colors.Hex("#0F172A"),
                TitleBarInactiveBackground = Colors.Hex("#1E293B"),
                TitleBarText = Colors.Hex("#E2E8F0"),
                TitleBarHover = Colors.Hex("#1E293B"),
                ToolbarBackground = Colors.Hex("#0F172A"),
                TabBarBackground = Colors.Hex("#1E293B"),
                TabInactiveBackground = Colors.Hex("#1E293B"),
                TabActiveBackground = Colors.Hex("#0F172A"),
                PanelBackground = Colors.Hex("#1E293B"),
                PanelFocusedBorder = Colors.Hex("#818CF8"),
                ScrollbarThumbBackground = Colors.Hex("#64748B"),
                ScrollbarThumbHoverBackground = Colors.Hex("#94A3B8"),
                ScrollbarThumbActiveBackground = Colors.Hex("#CBD5E1"),
                ScrollbarThumbBorder = Colors.Hex("#475569"),
                ScrollbarTrackBorder = Colors.Hex("#334155"),
                Success = Colors.Hex("#4ADE80"),
                SuccessBackground = Colors.Hex("#14532D"),
                Warning = Colors.Hex("#FBBF24"),
                WarningBackground = Colors.Hex("#78350F"),
                Error = Colors.Hex("#F87171"),
                ErrorBackground = Colors.Hex("#7F1D1D"),
                Info = Colors.Hex("#60A5FA"),
                InfoBackground = Colors.Hex("#1E3A8A"),
                LinkTextHover = Colors.Hex("#A5B4FC"),
            }
            : new GpuiThemeColors();

    internal static GpuiThemeColors FromJson(JsonElement source, GpuiThemeAppearance appearance)
    {
        var defaults = CreateDefault(appearance);
        return new GpuiThemeColors
        {
            Border = ReadColor(source, defaults.Border, "border"),
            BorderVariant = ReadColor(source, defaults.BorderVariant, "border.variant"),
            BorderFocused = ReadColor(source, defaults.BorderFocused, "border.focused"),
            BorderSelected = ReadColor(source, defaults.BorderSelected, "border.selected"),
            BorderTransparent = ReadColor(source, defaults.BorderTransparent, "border.transparent"),
            BorderDisabled = ReadColor(source, defaults.BorderDisabled, "border.disabled"),
            ElevatedSurfaceBackground = ReadColor(
                source,
                defaults.ElevatedSurfaceBackground,
                "elevated_surface.background"
            ),
            SurfaceBackground = ReadColor(source, defaults.SurfaceBackground, "surface.background"),
            Background = ReadColor(source, defaults.Background, "background"),
            ElementBackground = ReadColor(source, defaults.ElementBackground, "element.background"),
            ElementHover = ReadColor(source, defaults.ElementHover, "element.hover"),
            ElementActive = ReadColor(source, defaults.ElementActive, "element.active"),
            ElementSelected = ReadColor(source, defaults.ElementSelected, "element.selected"),
            ElementDisabled = ReadColor(source, defaults.ElementDisabled, "element.disabled"),
            GhostElementBackground = ReadColor(
                source,
                defaults.GhostElementBackground,
                "ghost_element.background"
            ),
            GhostElementHover = ReadColor(
                source,
                defaults.GhostElementHover,
                "ghost_element.hover"
            ),
            GhostElementActive = ReadColor(
                source,
                defaults.GhostElementActive,
                "ghost_element.active"
            ),
            GhostElementSelected = ReadColor(
                source,
                defaults.GhostElementSelected,
                "ghost_element.selected"
            ),
            GhostElementDisabled = ReadColor(
                source,
                defaults.GhostElementDisabled,
                "ghost_element.disabled"
            ),
            Text = ReadColor(source, defaults.Text, "text"),
            TextMuted = ReadColor(source, defaults.TextMuted, "text.muted"),
            TextPlaceholder = ReadColor(source, defaults.TextPlaceholder, "text.placeholder"),
            TextDisabled = ReadColor(source, defaults.TextDisabled, "text.disabled"),
            TextAccent = ReadColor(source, defaults.TextAccent, "text.accent", "accent"),
            TextOnAccent = ReadColor(source, defaults.TextOnAccent, "text.on_accent"),
            Accent = ReadColor(source, defaults.Accent, "accent"),
            AccentHover = ReadColor(source, defaults.AccentHover, "accent.hover"),
            AccentActive = ReadColor(source, defaults.AccentActive, "accent.active"),
            Icon = ReadColor(source, defaults.Icon, "icon"),
            IconMuted = ReadColor(source, defaults.IconMuted, "icon.muted"),
            IconDisabled = ReadColor(source, defaults.IconDisabled, "icon.disabled"),
            IconPlaceholder = ReadColor(source, defaults.IconPlaceholder, "icon.placeholder"),
            IconAccent = ReadColor(source, defaults.IconAccent, "icon.accent"),
            StatusBarBackground = ReadColor(
                source,
                defaults.StatusBarBackground,
                "status_bar.background"
            ),
            TitleBarBackground = ReadColor(
                source,
                defaults.TitleBarBackground,
                "title_bar.background"
            ),
            TitleBarInactiveBackground = ReadColor(
                source,
                defaults.TitleBarInactiveBackground,
                "title_bar.inactive_background"
            ),
            TitleBarText = ReadColor(source, defaults.TitleBarText, "title_bar.text"),
            TitleBarHover = ReadColor(source, defaults.TitleBarHover, "title_bar.hover"),
            TitleBarCloseHover = ReadColor(
                source,
                defaults.TitleBarCloseHover,
                "title_bar.close_hover"
            ),
            ToolbarBackground = ReadColor(source, defaults.ToolbarBackground, "toolbar.background"),
            TabBarBackground = ReadColor(source, defaults.TabBarBackground, "tab_bar.background"),
            TabInactiveBackground = ReadColor(
                source,
                defaults.TabInactiveBackground,
                "tab.inactive_background"
            ),
            TabActiveBackground = ReadColor(
                source,
                defaults.TabActiveBackground,
                "tab.active_background"
            ),
            PanelBackground = ReadColor(source, defaults.PanelBackground, "panel.background"),
            PanelFocusedBorder = ReadColor(
                source,
                defaults.PanelFocusedBorder,
                "panel.focused_border"
            ),
            ScrollbarThumbBackground = ReadColor(
                source,
                defaults.ScrollbarThumbBackground,
                "scrollbar.thumb.background"
            ),
            ScrollbarThumbHoverBackground = ReadColor(
                source,
                defaults.ScrollbarThumbHoverBackground,
                "scrollbar.thumb.hover_background"
            ),
            ScrollbarThumbActiveBackground = ReadColor(
                source,
                defaults.ScrollbarThumbActiveBackground,
                "scrollbar.thumb.active_background"
            ),
            ScrollbarThumbBorder = ReadColor(
                source,
                defaults.ScrollbarThumbBorder,
                "scrollbar.thumb.border"
            ),
            ScrollbarTrackBackground = ReadColor(
                source,
                defaults.ScrollbarTrackBackground,
                "scrollbar.track.background"
            ),
            ScrollbarTrackBorder = ReadColor(
                source,
                defaults.ScrollbarTrackBorder,
                "scrollbar.track.border"
            ),
            Success = ReadColor(source, defaults.Success, "success"),
            SuccessBackground = ReadColor(source, defaults.SuccessBackground, "success.background"),
            Warning = ReadColor(source, defaults.Warning, "warning"),
            WarningBackground = ReadColor(source, defaults.WarningBackground, "warning.background"),
            Error = ReadColor(source, defaults.Error, "error"),
            ErrorBackground = ReadColor(source, defaults.ErrorBackground, "error.background"),
            Info = ReadColor(source, defaults.Info, "info"),
            InfoBackground = ReadColor(source, defaults.InfoBackground, "info.background"),
            LinkTextHover = ReadColor(source, defaults.LinkTextHover, "link_text.hover"),
        };
    }

    private static Color ReadColor(
        JsonElement source,
        Color fallback,
        params ReadOnlySpan<string> names
    )
    {
        foreach (var property in source.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (!Matches(property.Name, name))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    return fallback;
                }
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException($"Theme color '{property.Name}' must be a string.");
                }

                try
                {
                    return Colors.Hex(property.Value.GetString()!);
                }
                catch (FormatException exception)
                {
                    throw new JsonException(
                        $"Theme color '{property.Name}' has an invalid value.",
                        exception
                    );
                }
            }
        }
        return fallback;
    }

    private static bool Matches(string actual, string expected) =>
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
        || actual.Equals(expected.Replace('.', '_'), StringComparison.OrdinalIgnoreCase)
        || actual.Equals(ToCamelCase(expected), StringComparison.OrdinalIgnoreCase);

    private static string ToCamelCase(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        var upper = false;
        foreach (var character in value)
        {
            if (character is '.' or '_')
            {
                upper = true;
                continue;
            }
            result.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }
        return result.ToString();
    }
}

/// <summary>
/// An immutable semantic theme that can be configured in C# or loaded from JSON.
/// </summary>
public sealed class GpuiTheme
{
    /// <summary>Default light theme used by element-only render contexts.</summary>
    public static GpuiTheme Default { get; } = CreateDefault(GpuiThemeAppearance.Light);

    public GpuiTheme(
        string name,
        GpuiThemeColors colors,
        GpuiThemeAppearance appearance = GpuiThemeAppearance.Light,
        GpuiThemeTypography? typography = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(colors);
        if (!Enum.IsDefined(appearance))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance));
        }

        Name = name;
        Appearance = appearance;
        Colors = colors;
        Typography = typography ?? GpuiThemeTypography.CreateDefault(appearance);
    }

    public string Name { get; }
    public GpuiThemeAppearance Appearance { get; }
    public GpuiThemeColors Colors { get; }
    public GpuiThemeTypography Typography { get; }

    public static GpuiTheme CreateDefault(GpuiThemeAppearance appearance, string? name = null) =>
        new(
            name ?? (appearance == GpuiThemeAppearance.Dark ? "Default Dark" : "Default Light"),
            GpuiThemeColors.CreateDefault(appearance),
            appearance
        );

    /// <summary>
    /// Loads a theme object, a <c>colors</c> settings object, or a Zed-style theme family.
    /// For a family, the first theme is selected unless <paramref name="themeName"/> is set.
    /// </summary>
    public static GpuiTheme FromJson(string json, string? themeName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A GPUI theme JSON document must be an object.");
        }

        var selected = SelectTheme(root, themeName);
        var name = ReadString(selected, "name") ?? ReadString(root, "name") ?? "JSON Theme";
        var appearance =
            ReadAppearance(selected) ?? ReadAppearance(root) ?? GpuiThemeAppearance.Light;
        var colors = GpuiThemeColors.FromJson(FindColors(selected), appearance);
        var typography = GpuiThemeTypography.FromJson(FindTypography(selected), appearance);
        return new GpuiTheme(name, colors, appearance, typography);
    }

    /// <summary>Loads a theme JSON file. Zed-style theme families may be selected by name.</summary>
    public static GpuiTheme LoadJson(string path, string? themeName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromJson(File.ReadAllText(path), themeName);
    }

    private static JsonElement SelectTheme(JsonElement root, string? themeName)
    {
        if (root.TryGetProperty("themes", out var themes))
        {
            if (themes.ValueKind != JsonValueKind.Array || themes.GetArrayLength() == 0)
            {
                throw new JsonException(
                    "The theme family's 'themes' value must be a non-empty array."
                );
            }

            foreach (var theme in themes.EnumerateArray())
            {
                if (theme.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("Each theme family entry must be an object.");
                }
                if (
                    themeName is not null
                    && string.Equals(
                        ReadString(theme, "name"),
                        themeName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return theme;
                }
            }

            if (themeName is not null)
            {
                throw new KeyNotFoundException(
                    $"Theme '{themeName}' was not found in the JSON family."
                );
            }
            return themes[0];
        }

        if (
            root.TryGetProperty("theme", out var wrapped)
            && wrapped.ValueKind == JsonValueKind.Object
        )
        {
            return wrapped;
        }
        return root;
    }

    private static JsonElement FindColors(JsonElement theme)
    {
        if (
            theme.TryGetProperty("colors", out var colors)
            && colors.ValueKind == JsonValueKind.Object
        )
        {
            return colors;
        }
        if (theme.TryGetProperty("style", out var style) && style.ValueKind == JsonValueKind.Object)
        {
            if (
                style.TryGetProperty("colors", out var nested)
                && nested.ValueKind == JsonValueKind.Object
            )
            {
                return nested;
            }
            return style;
        }
        return theme;
    }

    private static JsonElement FindTypography(JsonElement theme)
    {
        if (
            theme.TryGetProperty("typography", out var typography)
            && typography.ValueKind == JsonValueKind.Object
        )
        {
            return typography;
        }
        if (
            theme.TryGetProperty("style", out var style)
            && style.ValueKind == JsonValueKind.Object
            && style.TryGetProperty("typography", out var nested)
            && nested.ValueKind == JsonValueKind.Object
        )
        {
            return nested;
        }
        return theme;
    }

    private static string? ReadString(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static GpuiThemeAppearance? ReadAppearance(JsonElement source)
    {
        var value = ReadString(source, "appearance");
        if (value is null)
        {
            return null;
        }
        if (Enum.TryParse<GpuiThemeAppearance>(value, true, out var appearance))
        {
            return appearance;
        }
        throw new JsonException($"Unknown theme appearance '{value}'.");
    }
}
