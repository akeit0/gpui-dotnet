# Surface and interaction styling

GPUI.NET uses the pinned, unmodified GPUI text-inheritance and interaction machinery. An
application chooses matching colors; native GPUI selects hover and pressed presentation. Product
variants remain application-owned implementations of `IGpuiElementStyle<TTag>`.

## Matched surfaces

`SurfaceColors` contains a background and its matching foreground. `.Surface(...)` declares the
ordinary `Background` and `TextColor` operations together:

```csharp
var colors = ui.Theme.Colors;
return ui.VStack(content)
    .Surface(new(colors.SurfaceBackground, colors.Text))
    .BorderColor(colors.BorderVariant);
```

Plain descendants inherit the actual foreground. An explicit child `TextColor` overrides it.
Native components may establish their own defaults: a nested Button owns its presentation,
while Input text inherits through its wrapper. Input placeholder, caret, and selection, Slider
parts, raster images, and Drawing paints retain their separate color contracts.

A raw `.Background(...)` changes background paint only. It never infers a foreground. Colors
retain their alpha, including zero; transparent is a color rather than an omitted declaration.

## Complete interaction colors

`InteractionColors` contains three `SurfaceColors` values: `Normal`, `Hover`, and `Pressed`.
`.Paint(...)` writes all six background/text operations on a styled interactive element:

```csharp
internal readonly record struct NavigationStyle(GpuiTheme Theme, bool Selected)
    : IGpuiElementStyle<ButtonTag>
{
    public Element<ButtonTag> Apply(Element<ButtonTag> button)
    {
        var c = Theme.Colors;
        var colors = Selected
            ? new InteractionColors(
                new(c.Accent, c.TextOnAccent),
                new(c.AccentHover, c.TextOnAccent),
                new(c.AccentActive, c.TextOnAccent))
            : new InteractionColors(
                new(c.ElementBackground, c.Text),
                new(c.ElementHover, c.Text),
                new(c.ElementActive, c.Text));
        return button.Paint(colors);
    }
}
```

Selection, validation, and other product facts choose the recipe during managed rendering. A
recipe must stay pure. Native pointer state changes use the accepted operations without calling
managed `Render()`. Layout metrics, focus, and control parts are separate from these paint values.

`Surface` and `Paint` are managed compositions of existing operations. They add no component,
schema operation, FFI call, retained resource, stylesheet registry, or native dependency patch.
`Paint` writes six 24-byte operations; `Surface` writes two. Their value types contain only colors.

## Declaration order and compatibility

Styles have no special priority. Component defaults come first, then explicit operations in
declaration order. Later setters replace matching properties within their own state:

| Declaration | Effect |
| --- | --- |
| `Surface(pair).TextColor(red)` | Replaces the base foreground; keeps the paired background. |
| `TextColor(red).Surface(pair)` | Uses the pair's base background and foreground. |
| `Paint(states).TextColor(red)` | Replaces only the normal foreground. Hover and pressed retain their explicit foregrounds. |
| `Paint(states).HoverBackground(red)` | Replaces only the hover background; its foreground still comes from the recipe. Recheck that pair's contrast. |
| A second `Paint(states)` | Replaces all six surface colors at that position. |
| Omitting a declaration in the next snapshot | Removes that override; native defaults and remaining declarations determine presentation. |

Hover refines base; pressed refines applicable hover. `Paint` declares an explicit pressed
surface, so it does not use the legacy hover-only opacity fallback. Existing individual setters
retain that fallback (0.72). Disabled controls suppress interactive refinements and multiply
authored opacity by 0.5. Focus rings remain independent of borders and layout.

Enter/Space activation uses the existing foundation behavior. The pinned GPUI stores pending
keyboard activation separately from pointer active state; do not assume a held activation key
displays `Pressed` colors. See the [upstream proposal](proposals/GPUI_CONTENT_COLORS.md).

Theme changes still rerender managed fragments and invalidate virtual row batches because
recipes resolve theme tokens to literal colors. Presentation changes do not change retained
resource keys or reset Input editing, collection state, or Dock structure.

## Secondary content in composite controls

There is one inherited text foreground in the pinned GPUI. GPUI.NET does not expose ambient
Primary/Secondary roles. A composite recipe can expose a typed secondary child style, as
`BoardButtonStyle.SecondaryContent` does in TaskBoard. That explicit color must be readable on
all three of the owning control's backgrounds. Do not use global `TextMuted` without checking
the actual surface. Accent surfaces can use the same foreground for both emphasis levels.

When secondary text must follow changing parent foregrounds, let it inherit and use typography
or spacing for emphasis. A child literal will not follow the parent's hover/pressed text color.
Adding child hover listeners does not model parent interaction: pointer position, keyboard
activation, nested controls, and deferred content have different boundaries.

Table header content should use the header's matched pair; header buttons establish their own
recipes. Deferred body containers should declare their own surface when they represent a menu,
dialog, or tooltip. Do not assume an arbitrary trigger palette or Dock tab label colors are the
correct colors for their bodies.

Test foregrounds against their matching backgrounds in every supported application theme and
interaction state. An explicit secondary color needs checks against every state background.
For transparent colors, first composite against the known enclosing surface. Do not guess an
opaque backing or claim contrast from raw RGB values with alpha discarded. Selected controls
also need an appropriate non-color cue; color recipes do not provide accessibility semantics.

The general capability needed for native contextual roles is described in the
[GPUI content-color proposal](proposals/GPUI_CONTENT_COLORS.md). It is an upstream design proposal,
not an API implemented by GPUI.NET or a commitment to maintain a GPUI fork.
