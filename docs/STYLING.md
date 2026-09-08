# Surface and interaction styling

GPUI.NET uses the pinned, unmodified GPUI text-inheritance and interaction machinery. An
application chooses matching colors; native GPUI selects hover and pressed presentation. Product
variants remain application-owned implementations of `IGpuiElementStyle<TTag>`.

## Native presentation

Styled elements support the generated fluent operations declared by `bindings/schema.json`,
including layout, dimensions, uniform and per-side/axis margins, padding, and gaps, min/max
sizes, flex basis/shrink/wrap, container and self alignment, relative/absolute positioning with
offsets, overflow clipping, opacity, cursor, text alignment and clamping, backgrounds, borders,
text color, and typography. Interactive
elements additionally support native hover and active paint operations. An explicit `Cursor`
takes precedence over the pointing-hand default on interactive elements. Stacking stays with
deferred layers and declaration order: GPUI exposes no z-index knob.

The application theme supplies semantic tokens:

```csharp
var card = ui.VStack(content)
    .Padding(Px(16))
    .Surface(new(ui.Theme.Colors.SurfaceBackground, ui.Theme.Colors.Text))
    .BorderColor(ui.Theme.Colors.BorderVariant);
```

Native controls receive a resolved subset of the same theme. Product variants remain in the
application:

```csharp
internal readonly record struct PrimaryButtonStyle(GpuiTheme Theme)
    : IGpuiElementStyle<ButtonTag>
{
    public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
        button
            .Paint(new InteractionColors(
                new(Theme.Colors.Accent, Theme.Colors.TextOnAccent),
                new(Theme.Colors.AccentHover, Theme.Colors.TextOnAccent),
                new(Theme.Colors.AccentActive, Theme.Colors.TextOnAccent)));
}
```

`.Style(value)` invokes the typed recipe and returns the normal element builder. A later fluent call
can override a value. Do not add application variant enums or style objects to the native ABI.

`SurfaceColors` and `InteractionColors` pair backgrounds with inherited foregrounds. `Surface`
and `Paint` write existing operations; no additional schema or native state is needed. `Paint`
declares every state's foreground explicitly, so a subsequent `TextColor` overrides only the
normal state. See [Styling](STYLING.md) for the full contract and current inheritance limits.

Composite control recipes should resolve their backgrounds and content colors together. Let primary
content inherit the control's text color. If a child needs secondary emphasis, expose a typed child
style from the same resolved recipe, as TaskBoard's `BoardButtonStyle.SecondaryContent` does. A
global `TextMuted` color is not necessarily readable on a selected or pressed background. Secondary
content can share the primary foreground on accent surfaces; do not assume reduced opacity or a
muted color is always appropriate. Verify both foregrounds against normal, hover, and active
backgrounds in each supported application theme. These are application-owned style decisions;
explicit child colors still override inheritance and are not automatically recolored by GPUI.

Native component defaults are applied before explicit operations. Operations affecting the same
property apply in declaration order, including pixel and percentage forms. For ordinary growing
snapshot elements, `.Grow()` supplies zero minimum width and height so flex content can shrink;
explicit `MinWidth` and `MinHeight` override those defaults regardless of where `.Grow()` appears.

Interaction presentation follows a shared native contract:

- Component defaults establish the base; application operations and style recipes override them
  in declaration order. Product states such as selected or invalid are resolved by those recipes.
- Hover paint overrides the base, and active paint overrides hover for the properties it declares.
  With neither palette declared, the theme supplies hover/active backgrounds. An explicit hover
  palette with no active palette retains its colors while pressed feedback multiplies authored
  opacity by 0.72.
- Button, Checkbox, Radio, List, Table, and Slider keyboard focus uses a two-pixel outer ring in
  the theme's focused-border color. This paint layer preserves application borders, shadows,
  dimensions, and padding. It follows ancestor clipping and does not change item measurement or
  viewport geometry. Input retains its native caret/selection focus presentation.
- Disabled Button, Checkbox, Radio, Input, and Slider multiply their authored opacity by 0.5.
  An omitted opacity starts at 1; an explicit zero remains invisible. Disabled interactive controls
  do not install hover/active feedback. Disabled behavior remains in the existing native control.

These transitions remain native and require no managed render callback. Focus paint is independent
of application border colors, so keyboard focus does not replace a recipe's validation border.

Retained controls have separate internal presentation. Input text inherits typography and text color
through its wrapper. `PlaceholderColor`, `CaretColor`, and `SelectionColor` override its native text
parts and compose with `IGpuiElementStyle<InputTag>` recipes. Omitted parts use the current theme:
placeholder text, accent caret, and accent selection at alpha 0x40. Explicit selection color preserves
the supplied alpha. These declarations update presentation without replacing text, selection, IME
composition, focus, or revision. Omitting a previous override on a later render restores its theme
default. Other internal control parts require focused APIs rather than wrapper styling.

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

Theme changes still rerender managed fragments and invalidate virtual item batches because
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
