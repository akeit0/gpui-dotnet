# Contributing

## Choose the correct UI state class first

Before adding a component, classify it:

1. snapshot-only semantic component;
2. retained native container/resource (Scroll-like);
3. virtualized/batched datasource (List-like);
4. long-lived native entity (Input/Editor-like);
5. app/window service.

Do not add per-property or per-row P/Invoke APIs simply because the underlying GPUI method exists.

## Snapshot component workflow

1. edit `bindings/schema.json`;
2. assign semantic capabilities and adapter;
3. run repository binding generation;
4. implement/update Rust native adapter;
5. add managed ergonomics only where schema generation is insufficient;
6. validate both managed/native wire rules;
7. benchmark dirty render and clean repaint behavior.

## Retained resource workflow

A resource must have:

- a semantic render declaration;
- identity scoped by managed View + local key;
- explicit native ownership/lifetime pruning;
- UI-thread command handling;
- no raw Rust pointer exposed to C#;
- a controller only when imperative access is genuinely needed.

## Datasource workflow

A datasource must batch reverse ABI requests. For List, the Roslyn generator owns renderer IDs and direct dispatch. Avoid managed delegates/closures per item and avoid one reverse call per GPUI item request.

## Generation checks

Run:

```powershell
dotnet run --project tools/Gpui.Bindings.Generator -- generate
dotnet run --project tools/Gpui.Bindings.Generator -- verify
dotnet build gpui-dotnet.slnx
```

Native changes that touch `abi.rs` must regenerate `NativeMethods.g.cs` through the normal native build and keep `include/gpui_dotnet.h` synchronized.
