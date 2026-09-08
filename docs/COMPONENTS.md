# Components and retained resources

GPUI.NET exposes semantic components. C# builders write component IDs, typed operations, child
records, and UTF-8 data into the render arena. Rust chooses the concrete GPUI implementation through
the component's `NativeAdapter`.

The native application initializes the `gpui-base` behavior foundation. Button,
Checkbox, and Radio use foundation primitives for activation, focus, keyboard, accessibility, and
disabled behavior. Dock wears a small in-repo skin over the foundation layout engine, so the
default host links no styled component facade. Other adapters remain direct GPUI or GPUI.NET
implementations until their behavior families meet the migration parity criteria. Broad
`gpui-component` facilities link only into custom hosts that select them, such as the optional
editor host.

## Component classes

There are three implementation classes:

1. Snapshot components are rebuilt from the retained snapshot, such as Div, Text, Button, Badge,
   Divider, Image, and deferred-layer declarations.
2. Retained resources preserve mutable native state across managed renders: Scroll, List, Table,
   Input, Slider, and Dock.
3. Managed compositions combine existing primitives without adding an ABI component. Dialog,
   Sheet, and the shared title-bar helper use this model.

Optional native families use a fourth boundary: a typed schema assembly wraps the generic
NativeExtension node, while a custom host links the matching Rust provider at build time. The base
managed and native packages contain no extension-specific component contract. See
[EXTENSIONS.md](EXTENSIONS.md).

Choose the simplest class that satisfies the behavior. A component needs a retained resource only
when interaction state must survive independently from managed renders.

## Reference by topic

| Topic | Reference |
| --- | --- |
| Themes, recipes, inheritance, native presentation | [Styling](STYLING.md) |
| Scroll, List, Table, virtual items | [Collections](COLLECTIONS.md) |
| Input, Slider, Dock, controller behavior | [Retained controls](CONTROLS.md) |
| Focus, shortcuts, observers, accessible names | [Interaction](INTERACTION.md) |
| Overlays, tooltips, menus, window chrome | [Deferred layers](LAYERS.md) |

## Snapshot components

### Layout and content

- `Div`, `VStack`, and `HStack` form layout containers.
- `Text` carries UTF-8 content and inherits text styles normally.
- `Spacer` consumes available flex space.
- `Divider` provides a themed one-pixel separator by default.

### Interactive and display

- `Button`, `Checkbox`, and `Radio` use `gpui-base` behavior with stable GPUI.NET element identity.
  Their descendant Text content supplies the accessible name. `Disabled(bool)` prevents pointer and
  keyboard activation and removes the control from focus traversal.
- `Checkbox` and `Radio` use the controlled `Checked(bool)` operation. Foundation change requests
  translate back into the existing managed click callback; the next managed snapshot remains
  authoritative.
- Mounted and virtual-item Button, Checkbox, and Radio use the same native presentation builder
  for child composition, accessible names/descriptions, authored styles, and interaction/disabled
  paint. Each caller supplies its own stable identity and event route. Item callbacks retain their
  item-ID payload fallback; mounted controls retain View observers and shortcut routing.
- `Badge` provides minimal native defaults that managed styling can override.
- `Image` uses GPUI's decoder and cache. Its data is a filesystem path; presentation supports
  object fit and grayscale.
- `Drawing` owns ordered `Path` children and paints them through GPUI's native path tessellator.
  `ViewBox` independently maps each coordinate axis into the final layout bounds, making plot
  geometry responsive without a managed paint callback. Stroke widths stay in
  device-independent pixels. `Circle` uses the smaller ViewBox axis scale for both radii so point
  markers remain circular; `Ellipse` scales each radius independently. A `Path` must be attached
  directly to exactly one `Drawing`.
  Repeated native repaints can reuse tessellated geometry within the decoded snapshot. The first
  use of new bounds renders without retaining geometry; the second admits it to a bounded cache.
  Bounds include the origin and padding, so movement and resize rebuild geometry. A new accepted
  snapshot clears the cache, including changes to path commands, view box, colors, fill/stroke, and
  theme-resolved styles. Current clipping, opacity, and device scale still apply at paint time.
- `Dynamic` is a transparent one-child wrapper. While active, native GPUI schedules one managed
  render per display frame for the wrapper's owning View. Multiple wrappers for one View are
  deduplicated. The application remains responsible for time, interpolation, and stopping.

Image failures materialize a themed fallback. URI loading is not part of the current component
contract.

Use `Dynamic` for application-defined visual state such as chart interpolation. Continuous native
interaction such as scrolling, text editing, and slider dragging should remain in its retained
native resource.

## Adding a component

For a snapshot component:

1. Add the component and operations to `bindings/schema.json`.
2. Run the semantic generator.
3. Add or update the Rust adapter and materializer behavior.
4. Add managed validation, native validation, and materialization tests.
5. Add a small sample route only when it demonstrates behavior not covered elsewhere.
6. Update this document and [ABI.md](ABI.md) if the wire contract changed.

For a retained resource, also define stable identity, configuration reconciliation, command/event
semantics, teardown, pending-command behavior, and the high-frequency ownership boundary before
adding public API.

For an optional component family, keep its typed managed contract in a separate schema assembly,
register that schema in `bindings/extensions.json`, and link its Rust provider into an explicit
custom host. See [EXTENSIONS.md](EXTENSIONS.md).
