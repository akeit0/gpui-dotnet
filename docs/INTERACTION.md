# Focus, commands, and observers

Native GPUI owns focus and event propagation. Managed code declares targets and commands, and
receives only the events it subscribes to.

## Custom focus targets

`ui.FocusTarget(ref controller, container, tabStop: true)` makes an existing `Element<DivTag>`
(including VStack/HStack) a native focus target. It returns the same element, adding no wrapper.
Store a `FocusController` field in the owning View; its first declaration assigns a stable key.
Declare it on exactly one container per accepted snapshot and never share it between Views.

```csharp
private FocusController _preview;

// Inside Render:
var preview = ui.FocusTarget(ref _preview,
    ui.VStack(ui.Text("Preview")).Padding(Px(16)), tabStop: true)
    .OnShortcut(this, new(ShortcutKey.Right), static view => view.NextPreview());
```

Call `Focus()` from an event or accepted effect to move focus to the container itself. `Blur()`
releases focus only if that container is focused; it leaves a focused descendant alone. Commands
use the normal any-thread resource route and require an accepted declaration. Removing the
declaration retires its native identity and pending commands; reintroducing the same controller
creates a fresh target. Content changes, node reordering, and theme changes preserve identity.

Tab participation defaults to false and can change declaratively without moving current focus.
Pointer and programmatic focus remain available when Tab skips the target. GPUI handles pointer
focus natively, allowing child controls to take focus first. Keyboard focus uses the existing
theme focus ring without changing layout or application borders. Scoped shortcuts on the container
are reachable when it or its descendants hold focus, subject to native control key handling.

Use this declaration for custom containers. Existing native controls retain their own focus
ownership. Focus targets are unavailable in virtual row snapshots. Focus groups, roving selection,
and custom restoration policies are not exposed by this API. The sample's Keyboard focus page
demonstrates direct focus, Tab participation, scoped preview navigation, and returning to an Input.

## Scoped keyboard shortcuts

`OnKeyDown` and `OnKeyUp` are observers. Use `OnShortcut` on a Div container or Overlay when an
application command needs native matching and consumption:

```csharp
ui.VStack(content)
    .OnShortcut(this, new(ShortcutKey.S, ShortcutModifiers.Primary),
        static view => view.Save(), new ShortcutOptions(enabled: canSave));
```

`Primary` resolves to Command on macOS and Control on Windows/Linux. Modifiers match exactly;
Primary cannot be combined with explicit Control or Platform. The initial API supports a single
key (letters, digits, navigation/editing keys, F1–F24, or common punctuation), not multi-key chords.
Letters, digits, Space, and punctuation require Control, Platform, or Primary. Bare characters and
Shift/Alt-only text shortcuts are deliberately excluded until explicit mode/focus semantics exist.

Scopes follow native focus ancestry and do not create focus targets. Descendant bindings run first;
the last matching declaration on the same element wins. By default, an enabled match consumes the
key and invokes one managed `Action<TView>`. Disabled bindings reserve their gesture without invoking;
held-key repeats likewise reserve it unless `allowRepeat: true`. `consume: false` allows native
propagation and, for an enabled command, matching ancestor shortcuts to run as well.

`.IsolateShortcuts(true)` prevents ancestor shortcut commands while leaving unmatched keys available
to native controls, observers, and Tab traversal. Modal Dialog/Sheet/Overlay hosts isolate shortcuts
automatically. They retain their existing Escape dismissal and focus restoration. GPUI native
keybindings run before shortcut listeners. Ordinary text is outside the supported shortcut set;
events marked as character input by the platform (such as AltGr) do not invoke shortcuts.
Controls and editor providers do not need shortcut-specific text-protection hooks.

Bindings are part of accepted snapshots and use normal View-bound callback lifetime. Only a matched,
enabled command crosses the ABI. Unmatched keys, matching, precedence, and consumption stay native.
Declare shortcuts in ordinary View renders, outside virtual item batches. TaskBoard demonstrates
Primary+N/F/S/D page commands and Primary+Enter in its New Task dialog.

## Key and mouse observers

`Div`, `Button`, `Checkbox`, and `Radio` support observer `OnKeyDown`, `OnKeyUp`,
`OnMouseDown`, `OnMouseUp`, `OnModifiersChanged`, `OnHover`, `OnMouseDownOut`, `OnMouseUpOut`,
`OnMouseMove`, `OnScrollWheel`, and `OnFileDrop` bindings through the `key_mouse` capability. They reuse the
existing `control_event` channel and never consume the native event: Rust forwards
the key name (plus modifiers and held-repeat), the mouse position/button/click-count (plus
modifiers), the bare modifier state, hover transitions, movement/wheel deltas, or dropped file
paths without calling
`stop_propagation`, moving focus, or blocking default handling.
Focused Input editing, Slider keys, List/Table navigation, Overlay Escape, and menu triggers
therefore win first; a bound element only observes events that bubble to it. Movement and wheel
events are only published while bound, so unregistered elements cost nothing; registered
handlers must stay cheap.

Use `OnShortcut` for commands that need native matching and consumption. Use observers for
notifications and diagnostics, after native controls have handled the event.

`KeyEvent.Matches` compares the key name ordinal-ignore-case and requires exact modifiers, so
`Ctrl+Shift+S` does not match `Ctrl+S`. Holding a key produces OS key-repeat `Down` events with
`IsHeld` set, so one-shot hot-key actions should guard with `!key.IsHeld`. Modifier-only presses
(e.g. holding Ctrl alone) never produce key events in GPUI; track them with `OnModifiersChanged`,
which reports the current modifiers. Mouse movement and wheel events cross the ABI only when their
observer bindings are declared. These bindings are render-pass declarations like `OnClick`
(pure `Render`, state changes in the handler plus `Invalidate()`), and they are invalid inside
virtualized List/Table row snapshots, which have no mounted View lifetime.

## Accessible names and descriptions

Button, Checkbox, Radio, Input, and Slider accept `AccessibleName(...)` and
`AccessibleDescription(...)` in UTF-16 or UTF-8. These declarations set the name and supplementary
help on the native interactive control; they do not create visible labels or additional wrapper
accessibility nodes. For example:

```csharp
ui.Input("account", new InputOptions())
    .AccessibleName("Account name")
    .AccessibleDescription("Use the name shown on your account");
```

Explicit names override Button/Checkbox/Radio descendant-text inference, including in virtual
rows. Input and Slider do not infer names from placeholders or values. Declarations must be nonempty,
and the last declaration wins. Omit a declaration in a later snapshot to remove it (restoring text
inference where applicable). Updating retained control metadata preserves native values, selection,
composition, focus, and interaction state. Descriptions are resolved text, not references to other
elements; label/help/error relationships and platform screen-reader verification remain open work.

