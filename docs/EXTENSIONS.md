# Extensions and custom native hosts

An extension can add managed APIs, generators, semantic components, and native assets without
exposing Rust or GPUI types directly to C#.

## Managed-only extensions

Reference `GPUI.NET` for the normal application surface, or `GPUI.NET.Core` when the extension owns
its native dependency selection. Keep extension components in their own assembly and package.

Managed compositions and `IGpuiElementStyle<TTag>` variants require no native host change when they
use existing semantic components and operations.

## Custom native host contract

A host requiring Rust-side behavior ships a uniquely named native library for each supported RID.
It must export:

```c
const gpui_dotnet_api_v1* gpui_dotnet_get_api(uint32_t requested_version);
```

The managed runtime validates:

- the requested and reported ABI version;
- the API-table `struct_size` prefix;
- the semantic schema hash;
- all required function entries.

The host must preserve the record layouts, ownership rules, pointer/length validation, callback
semantics, and panic barriers documented in [ABI.md](ABI.md). A matching Rust compiler ABI is not a
substitute for this C contract.

## Selecting a host

Choose the native library before opening or running the application:

```csharp
using Gpui;

var application = new GpuiApplication(
    new NativeRuntimeOptions
    {
        LibraryPath = Path.Combine(
            AppContext.BaseDirectory,
            "my_gpui_extension_host.dylib"
        ),
    }
);

application.OpenWindow(new MainView());
application.Run();
```

The single-window convenience overload accepts the same runtime options. When `LibraryPath` is not
set, GPUI.NET resolves its packaged `gpui_dotnet` host for the current RID.

## Packaging guidance

- use a unique native file name so the extension and base host can coexist;
- put each native library under the extension package's `runtimes/<rid>/native` path;
- build each RID on its natural platform;
- version the managed package and native assets together;
- test discovery, ABI/schema rejection, application startup, and a clean consumer restore;
- own extension-specific staging and packing scripts rather than modifying base-package checks to
  accept unrelated assets.

If the extension changes the semantic schema, it owns a complete compatible managed/native pair.
An application cannot combine independently generated semantic registries in one GPUI.NET host.
