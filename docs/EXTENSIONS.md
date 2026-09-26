# Extensions and custom native hosts

Extensions keep optional component families out of the default managed API and native package.
The contract deliberately separates an extension's managed schema from its Rust runtime.

## Boundary

`GPUI.NET.Core` contains a generic `NativeExtension` semantic envelope, command/event transports,
and host negotiation. It does not contain extension-specific component kinds, configuration
fields, typed controllers or events, or native implementations.

An extension consists of two independently packaged halves:

```text
extension schema assembly
  typed C# builders + options + schema ID/version/hash

custom native host
  gpui-dotnet runtime + selected Rust providers, linked into one binary
```

The schema assembly references `GPUI.NET.Core`. It wraps `RenderContext.NativeExtension` with a
typed API and writes an opaque UTF-8 configuration owned by that extension schema. Extension
definitions do not enter `bindings/schema.json` and do not change the base semantic schema hash.

`bindings/extensions.json` registers extension schema files and their generated C#/Rust outputs.
The normal binding generator canonicalizes each schema, derives its independent hash, and emits the
shared identity, component-kind, flag, command, and event constants. A component's ordered `lines`
configuration fields also generate an invariant managed encoder plus the matching validating Rust
parser and enum types. Hand-maintained protocol numbers and duplicate configuration parsers are not
part of an extension implementation.
JSON-encoded string fields allow multiline content within the line-based configuration envelope;
both generated sides reject NUL, which is reserved by the generic node transport.

Rust providers implement `gpui_dotnet::extension::NativeExtension`. A custom host calls
`install_native_extensions` once and delegates its `gpui_dotnet_get_api` export to
`gpui_dotnet::api`. The runtime crate is an `rlib`; explicit default and custom `cdylib` host crates
own the native entry-point exports. GPUI and Rust values never cross a dynamic-library boundary.
Providers that render asset-backed native elements expose an `asset_source`; the host composes all
installed provider sources before GPUI starts. The default host remains asset-free.

Runtime loading arbitrary Rust plugin DLLs is intentionally unsupported. Rust has no stable ABI,
and separately linked GPUI revisions would create incompatible type universes. Combining multiple
native extensions requires building one host with all selected providers.

## Compatibility

Every extension has:

- a stable ASCII identifier;
- an independent protocol version;
- a deterministic 64-bit schema hash;
- one or more component-kind identifiers.

`NativeRuntimeOptions.Extensions` lists the schemas required by an application. The native ABI's
`supports_extension` entry verifies every ID/version/hash before the event loop starts. An extension
node repeats that identity in its envelope, so a declaration cannot accidentally reach a provider
built from another schema.

The retained resource identity is `(session, owner View, extension ID, component kind, key,
version, schema hash)`. Extension state lives in a type-erased store owned by the managed View's
native resource store and is dropped when the committed snapshot stops declaring it.

Typed schema packages wrap `NativeExtensionController`, normally through a factory on
`ViewConstruction`. The controller uses the stable any-thread View route and sends schema-owned command
IDs and opaque byte payloads through the generic ABI. A custom provider validates each command,
while Core queues copied payloads by the full resource identity until native materialization.
Typed schema packages also bind render-scoped callbacks through `NativeExtensionEventBinding` and
decode copied `NativeExtensionEvent` packets into their public event types. Event IDs, flags,
revisions, and payload layouts remain schema-owned.

## Optional component catalog

`src/Gpui.Components` is a separate managed schema project paired with the
`gpui-dotnet-components-host` custom host. It exposes semantic wrappers over official
`gpui-component` controls without putting that dependency in the default host. C# still owns the
tree, product state, options, and callbacks; Rust owns native rendering and frame-sensitive
interaction.

The host includes forty-one catalog families plus Editor, a retained extension example. Display
and content coverage includes Spinner, Skeleton, Separator, Badge, Tag, linear and circular
Progress, Alert, GroupBox, Label, Kbd, Avatar, Icon, ShimmerText, Attachment, Empty, StatusBar,
Bubble, BubbleGroup, Message, MessageGroup, Marker, and DescriptionList. Interactive and
controlled coverage includes Rating, Button, Link, Switch, Checkbox, Radio, Toggle, Pagination,
Collapsible, Toolbar, ToolbarGroup, Breadcrumb, Tabs, Select, Combobox, Tree, and Textarea. Form batches
a compound field layout with existing core or optional controls.

`Tree` uses `gpui-base::TreeState` for native expansion, keyboard cursor, and virtual row rendering;
the optional component layer supplies themed rows. A single preorder batch carries stable string
IDs, labels, depths, disabled flags, and initial expansion for newly declared IDs. Expansion state
survives accepted data replacement for surviving IDs. C# owns the committed selected ID, which is
separate from the native keyboard cursor. Pointer clicks and Space on the focused cursor request
selection; expansion and collapse emit ID events. The local batch is limited to 4096 nodes. Large
or remote trees need a batched datasource contract before they can use this API.

`Select` and `Combobox` share batched labels, stable nonzero IDs, disabled items, and controlled
selection. Native entities retain popup, keyboard, filtering, and scrolling state. Select requests
one ID or clearing; Combobox requests the full selected-ID set and supports multiple selection.
The next managed declaration decides which request to accept. Item and selection batches reconcile
by ID without rebuilding an unchanged popup. Search mode for Select and multiple mode for Combobox
are fixed for the lifetime of a retained key; change the key to change the mode. The catalog batch
is limited to 4096 local items; large or remote datasets need a separate batched datasource
contract.
Select has an accessible name field. The current upstream Combobox facade does not expose a
corresponding accessible-name hook.

`Form` takes one control child per field and an optional full-width footer. Labels, help text,
errors, required markers, column spans, label orientation, and grid columns form one declarative
batch. Error text replaces help text while present. C# owns field values and validation; the native
component owns the grid and themed label/error presentation. `ComponentFormField.For` sets a core
control's accessible name and current description from the field declaration. For extension
controls, `NamedControl` requires the control to declare its own accessible label (for example,
`ComponentTextareaOptions.AccessibilityLabel`). Separate accessible descriptions for optional
controls are not part of their current schema.

`Textarea` is an ordinary multiline field with keyed native value, selection, IME, undo, and
scrolling state. Its initial value is consumed when the resource is created; subsequent declarations
update placeholder, row count, disabled/read-only state, accessibility label, and callback binding.
`Rows` sets the visible field height and its native text viewport. User edits can emit a copied
UTF-8 value with a native revision. `Focus` and `SetValue` are coarse commands; a changed
replacement clears selection, scroll, and undo history without emitting a
change event, while an identical replacement preserves them. It uses the existing component host
and generic extension transport.

The Editor example uses its own schema within the same host to exercise bootstrap, retained state,
revisioned commands, and native edit events. Its separate schema is an example of the extension
contract, not a requirement to split every component family or host. Applications using both
schemas list both requirements. The [Editor contract](EDITOR.md) records the example's behavior;
the [Editor sample](../samples/Gpui.Editor.Sample/README.md) contains its run instructions.

Parent-capable controls receive one batched managed child list. Native callbacks use schema-owned
event IDs and payloads, while current values remain managed-authoritative.
Resolved GPUI.NET theme roles are projected into the component theme on startup and every theme
change.

`Attachment` accepts independent media, extra-content, and actions slots. Its title, description,
preview source, size, orientation, and lifecycle status are declarative; the application owns the file
model and upload work. A card click emits a typed event. Existing controls in the actions slot retain
their own event routes without activating the card. Use GPUI.NET's Scroll for attachment collections;
the attachment declaration does not create a separate managed file resource or datasource.

`Empty` provides themed media, title, description, content, and footer slots. Applications decide
when to show it and supply any controls in its content slot. The shared optional-slot transport keeps
each named child in its declared position even when earlier slots are absent.

`Toolbar` and `ToolbarGroup` host managed child controls while gpui-kit's native toolbar owns
roving keyboard navigation and group semantics. The toolbar's size sets container density; children
retain their explicitly declared sizes and event routes. Disabling toolbar navigation does not
disable its child controls, which remain application-owned declarations.

`StatusBar` accepts independent left, center, and right regions. Its native layout places the
outer regions at the edges and aligns the center according to which outer regions are present.
Each region can contain a managed composition of text, controls, or other elements.

`DescriptionList` batches label/value elements, column spans, and full-row separators into one
native list. Its orientation, size, border, label width, and column count are declarative. The
extension schema's `u32_list` field gives C# and Rust a shared typed span sequence; zero marks a
separator, and positive values mark item spans. The provider checks child count and span bounds
before handing items to gpui-kit.

`Breadcrumb` batches labels, stable item IDs, and disabled flags. The native component owns its
link roles, separators, and click behavior. One render-scoped event route returns the selected
item ID; applications own navigation and decide how the path changes.

`Tabs` batches labeled items and stable IDs with a controlled selected ID. The native tab bar
owns tab presentation and optional overflow; one event route returns the activated ID. The
application owns the selected content and updates the declaration after activation. The strip has
a keyboard tab stop: Left/Right wrap across enabled tabs, Home/End choose the first/last enabled
tab, and Enter/Space activate the current tab. Keyboard navigation emits the same selected-ID event
as a click; the next managed declaration is authoritative.

`Icon` renders an SVG from the native host's asset source with semantic size and optional color.
Asset paths prefixed with `app-assets/` resolve files beside the application executable; other
paths use the host's bundled gpui-kit assets.
`Avatar` accepts a name fallback and optional image source; the host retains image loading and
rendering behavior. Button can use the same asset paths for a leading icon.

Conversation composition uses `Bubble` for a themed content surface and optional reaction slot,
`Message` for avatar/header/body/footer alignment, `Marker` for separators and loading/status rows,
and the two group elements for spacing. The application owns message data and all actions. A
Message can request a list-item accessibility role, and a Marker can request a status role. Ghost
bubbles need `ContentHasGhostSurface` on their containing Message so header/footer insets match the
unstyled body. These elements accept batched child declarations; they do not add message storage or
per-item managed callbacks to the native host.

The [component sample](../samples/Gpui.Components.Sample/README.md) demonstrates the generated
contract and custom-host composition.

This is deliberately a semantic catalog, not a mirror of every Rust builder method. Broader
coverage should add coherent component families to the schema and provider. Retained data sources,
editors, overlays, and stateful compound controls need their own coarse ownership contracts rather
than being forced through a property bag.

## Packaging guidance

- keep schema assemblies free of native assets;
- keep each host's Rust dependency and feature graph explicit; an optional schema assembly does not
  provide isolation if its provider crate is still linked by the default host;
- link provider-only component families, parsers, grammars, and assets only into the custom hosts
  that select them;
- avoid depending on a monolithic component façade for one provider when a feature-gated or
  narrowly scoped runtime crate can preserve the same contract;
- use a unique native host file name so custom and default hosts can coexist;
- put release host libraries under RID-specific runtime packages;
- build every host on its natural target platform;
- version each schema and its provider together;
- test missing-extension and schema-mismatch rejection before application startup;
- test a clean consumer restore without requiring Cargo or a Rust toolchain;
- record release artifact sizes and check that optional providers do not enter the default host.
