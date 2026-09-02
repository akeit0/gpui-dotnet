# Binding generation

The repository has two generators with separate responsibilities.

## Semantic protocol generator

`tools/Gpui.Bindings.Generator` reads `bindings/schema.json` and produces:

- `src/Gpui/Rendering/Semantic.g.cs`;
- `crates/gpui-dotnet/src/semantic.g.rs`;
- matching component IDs, operation IDs, capabilities, adapters, value constraints, and schema
  hash on both sides.

It also reads `bindings/extensions.json`. Each registered optional schema produces a C# constants
file in its managed schema assembly and a matching Rust constants file in its native provider.
Extension IDs, versions, component kinds, flags, commands, and hashes therefore have one source of
truth.

Run:

```sh
dotnet run --project tools/Gpui.Bindings.Generator -- generate
dotnet run --project tools/Gpui.Bindings.Generator -- verify
```

`verify` fails when any committed base or extension output does not match its schema. Never edit a
generated semantic or extension file by hand.

The schema defines:

- component IDs and names;
- generated managed factory shape, when applicable;
- native adapter selection;
- capabilities such as styled, interactive, or checked;
- operation IDs, value kinds, managed API types, compatibility requirements, and scalar/payload
  constraints.

The generator emits capability-constrained fluent methods and equivalent managed/native validation
metadata. Put a new rule in the schema when both validators need it; do not duplicate a manual
switch unless the rule depends on component-specific structure that the schema cannot express.

Components with retained resources or special child invariants use manual `RenderContext` factories.
Their style operations and registry metadata are still generated.

Event operations store compact runtime tokens. `OnClick` may additionally store an unmanaged
`ulong` payload in `OpRecord.B`; it is not a delegate pointer or `GCHandle`.

## Roslyn source generator

`src/Gpui.Generators` runs in application compilations and generates view factories and virtual-row
dispatch.

### `[GpuiView]`

Apply `[GpuiView]` to a `partial` View type. The generator implements the AOT-safe
`IGeneratedViewFactory<T>` contract used by framework-owned `ui.Child<T>()` slots. No runtime
reflection is required.

Events are runtime fluent bindings (`OnClick`, `OnChanged`, `OnSubmitted`, `OnFocusChanged`,
`OnReleased`, and `OnDismiss`); the generator does not inspect ordinary event methods.

### `[GpuiListItem]`

Accepted methods are synchronous, non-generic instance methods with this shape:

```csharp
Element Row(int index, ref RenderContext ui)
```

`Element<TTag>` return types are also accepted. The generator emits:

- a deterministic nonzero renderer ID;
- a `Rows.Row` `ListItemRenderer` token;
- direct switch-based dispatch on the owning mounted View;
- diagnostics for invalid signatures, duplicate names, reserved members, and ID collisions.

A row renderer may be retried after native arena growth and follows the same purity rules as
`View.Render()`.

## Native C-layout generation

The native Cargo build uses `csbindgen` to update `src/Gpui/Interop/NativeMethods.g.cs` from the Rust
C-layout records and API table. Treat that file as generated. If `abi.rs` changes, run a native
build and include the regenerated managed output in the same change.

ABI changes also require managed/native size checks, tests, and [ABI.md](ABI.md) updates.
