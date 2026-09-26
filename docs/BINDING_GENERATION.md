# Binding generation

The repository has two generators with separate responsibilities.

## Semantic protocol generator

`tools/Gpui.Bindings.Generator` reads `bindings/schema.json` and produces:

- `src/Gpui/Rendering/Semantic.g.cs` (wire protocol: IDs and the validation registry);
- `src/Gpui/Rendering/SemanticElements.g.cs` (managed API: tags, style enums, factories, styling);
- `crates/gpui-dotnet/src/semantic.g.rs`;
- `docs/SEMANTIC_IDS.md` (the numeric protocol reference);
- matching component IDs, operation IDs, capabilities, adapters, value constraints, and schema
  hash on both sides.

It also reads `bindings/extensions.json`. Each registered optional schema produces a C# constants
file in its managed schema assembly and a matching Rust constants file in its native provider.
Extension IDs, versions, component kinds, flags, commands, and hashes therefore have one source of
truth.
An extension component can declare zero `lines` fields when it has no configuration. Its generated
managed encoder emits an empty string, and its native parser accepts only that empty string.
The `u32_list` field encodes a batch of invariant decimal values in one comma-separated line;
the generated parser rejects malformed items and returns a typed vector. The component provider
still validates the list's own bounds and relationship to child elements.

Run:

```sh
dotnet run --project tools/Gpui.Bindings.Generator -- generate
dotnet run --project tools/Gpui.Bindings.Generator -- verify
```

`verify` fails when any committed base or extension output does not match its schema. Never edit a
generated semantic or extension file by hand.

## Schema organization and IDs

`operationGroups` groups operations by subsystem, with an explicit `firstId`/`lastId` allocation.
Groups and operations are ordered by numeric ID. The generator rejects empty groups, overlapping
ranges, out-of-range operations, and duplicate IDs or names. Add an operation to its existing group
instead of appending unrelated entries to the schema. IDs are explicit; list position never assigns them.

The identity group occupies 1–99. Other groups each reserve one hundred IDs; the generated
[Semantic IDs](SEMANTIC_IDS.md#operations) table lists their ranges. Components are ordered by
authoring family. Resource command blocks start at `resourceId * 100`; control events use separate
hundred-ID blocks by event family, below the extension-event high bit. Enums keep explicit values,
including packed key/modifier policies; do not infer their meaning from array position.

Use generated names in routing and tests. Match named operations or events explicitly rather than
depending on numeric intervals. Numeric assignments may change during preview without aliases;
the semantic hash rejects mismatched managed/native builds. Rebuild both sides together.

## Generator implementation

The semantic generator is split by responsibility:

- `Program.cs` is the entry point; `BindingGenerator.Runner.cs` loads, hashes, and writes or verifies outputs.
- `Schema.cs` models source data; `BindingGenerator.Validation.cs` validates core declarations.
- `BindingGenerator.CSharp.cs` and `BindingGenerator.Rust.cs` emit language-specific code.
- `BindingGenerator.Extensions.cs` handles independent extension schemas.
- `BindingGenerator.Reference.cs` emits the ID catalog; `BindingGenerator.Names.cs` contains naming and capability helpers.

Handwritten generator sources use `dotnet csharpier format .`. Generated C# files retain emitter
formatting and are excluded from that command. Native generated output must also satisfy `cargo fmt`.

## Schema contents

The schema defines:

- component IDs and names;
- generated managed factory shape, when applicable;
- native adapter selection;
- capabilities such as styled, interactive, or checked;
- operation IDs, value kinds, managed API types, compatibility requirements, and scalar/payload
  constraints;
- packed shortcut validation of callback payload word B, alongside ordinary value-word A validation;
- `uint` style enums (C# names plus explicit wire values, e.g. `FlexWrap`, `MouseCursor`);
- per-operation `managedMethod` fluent APIs (`f32`/`f32x2`/`u32`/`u64`/`color` scalars with optional
  defaults and fail-fast guards, enum-typed parameters, `string` UTF-8 data payloads, or `strings`/
  `pairs` comma-joined list payloads recorded as arena offset/length pairs) and `lengthMethods` (`Length` overloads fanning out
  to paired pixel/percent operations such as `Width`);
- retained resource kinds and their command IDs, names, and documentation;
- control event IDs, family grouping, and documentation.

Resource and control-event IDs generate as `ResourceKind`/`ResourceCommandKind` and per-family
event-kind enums in C# plus `RESOURCE_*`/`COMMAND_*`/`EVENT_*` constants in Rust. Both sides
match on generated names only; the schema hash covers these sections, so renumbering either side
without the schema fails verification. Payload shapes, routing, and queueing stay hand-written:
the schema owns identities, not behavior.

The generator emits capability-constrained fluent methods and equivalent managed/native validation
metadata. Put a new rule in the schema when both validators need it; do not duplicate a manual
switch unless the rule depends on component-specific structure that the schema cannot express.

Components with retained resources or special child invariants use manual `RenderContext` factories.
Their style operations and registry metadata are still generated.

Event operations store compact runtime tokens. `OnClick` may additionally store an unmanaged
`ulong` payload in `OpRecord.B`; it is not a delegate pointer or `GCHandle`.

## Roslyn source generator

`src/Gpui.Generators` runs in application compilations and generates view factories and virtual-item
dispatch.

### `[GpuiView]`

Apply `[GpuiView]` to a `partial` View type. The generator implements the AOT-safe
`IGeneratedViewFactory<TView>` or `IGeneratedViewFactory<TView, TProps>` contract.
Generated `Spec()` / `Spec(props)` methods return lightweight typed declarations consumed by both
`ui.Child(key, spec)` and `application.OpenWindow(spec)`. No runtime reflection is required.
The factory receives `ViewConstruction` and initial props when applicable. If no constructor is
declared, the generator supplies that constructor; explicit constructors must support that signature.
Reserved generated members include `CreateGpuiView` and `Spec`.

Events are runtime fluent bindings (`OnClick`, `OnChanged`, `OnSubmitted`, `OnFocusChanged`,
`OnReleased`, `OnDismiss`, `OnKeyDown`, `OnKeyUp`, `OnMouseDown`, `OnMouseUp`,
`OnModifiersChanged`, `OnHover`, `OnMouseDownOut`, `OnMouseUpOut`, `OnMouseMove`,
`OnScrollWheel`, and `OnFileDrop`); the generator does not inspect ordinary event methods.

### `[GpuiListItem]`

Accepted methods are synchronous, non-generic instance methods with this shape:

```csharp
Element Item(int index, ref RenderContext ui)
```

Props-bearing Views instead require `Element Item(int index, in TProps props, ref RenderContext ui)`.
Dispatch supplies the owner's accepted props. `Element<TTag>` return types are also accepted.
The generator emits:

- a deterministic nonzero renderer ID;
- a `Items.Item` `ListItemRenderer` token;
- direct switch-based dispatch on the owning mounted View;
- diagnostics for invalid signatures, duplicate names, reserved members, and ID collisions.

An item renderer grows output before writes, without capacity retry, and follows the same purity rules as
`View.Render()`.

## Native C-layout generation

The native Cargo build uses `csbindgen` to update `src/Gpui/Interop/NativeMethods.g.cs` from the Rust
C-layout records and API table. Treat that file as generated. If `abi.rs` changes, run a native
build and include the regenerated managed output in the same change.

ABI changes also require managed/native size checks, tests, and [ABI.md](ABI.md) updates.
