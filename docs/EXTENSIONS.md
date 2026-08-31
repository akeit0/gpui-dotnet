# Extensions and custom native hosts

The `Gpui` package is the application-facing meta package. Future extension packages can
reference `Gpui.Core` for managed APIs and `Gpui.Runtime` for native assets, then add managed
controls, generators, semantic components, or platform assets without changing the base host.

An extension that needs Rust-side behavior may ship a separate native host with a unique file
name. The host must export:

```text
gpui_dotnet_get_api(uint requested_version)
```

The managed runtime still validates the API table size, ABI version, schema hash, and required
function entries. This keeps a custom host optional without accepting an unchecked Rust ABI.

Select the host explicitly at application startup:

```csharp
using Gpui;

var application = new GpuiApplication(
    new NativeRuntimeOptions
    {
        LibraryPath = Path.Combine(AppContext.BaseDirectory, "my_gpui_extension_host.dll"),
    }
);
application.OpenWindow(new MainView());
application.Run();
```

The same option is available on `GpuiApplication.Run(view, windowOptions, runtimeOptions)`.
When `LibraryPath` is omitted, the existing RID-native `gpui_dotnet` resolution is unchanged.

Extension package guidance:

- keep managed extension APIs in their own assembly/package and reference `Gpui`;
- put a custom native host under the extension package's RID-specific native assets;
- use a unique native file name when it must coexist with the base host;
- preserve the versioned GPUI.NET C ABI and rebuild against the compatible GPUI revision;
- treat the base `eng/pack.ps1` checks as base-package packaging only—an extension owns its own
  native asset matrix and packing rules.
