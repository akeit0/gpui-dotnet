# GPUI.NET

GPUI.NET is a semantic C# frontend for [GPUI](https://gpui.rs/), built for .NET 10 desktop
applications. Your application owns state and view logic in C# while the native Rust host handles
windows, layout, retained controls, and platform integration.

## Install

```sh
dotnet add package GPUI.NET
```

The `GPUI.NET` package brings the managed API and the native runtime package family. Native assets
are supplied for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`.

## Minimal application

```csharp
using Gpui;
using static Gpui.Units;

var application = new GpuiApplication();
application.OpenWindow(
    new MainView(),
    new GpuiWindowOptions { Title = "Hello GPUI.NET", Width = 900, Height = 600 }
);
application.Run();

[GpuiView]
internal sealed partial class MainView : View
{
    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.Text("Hello from GPUI.NET"),
                ui.Button("increment", "Increment")
            )
            .Gap(Px(12))
            .Padding(Px(20));
}
```

## Runtime requirements

- .NET SDK/runtime 10.
- A desktop environment supported by the selected runtime identifier.
- On Windows, `GPUI.NET` supplies a default Windows application manifest for executable projects.

The default manifest enables Common Controls v6 and Per-Monitor V2 DPI awareness. An explicitly
configured consumer `ApplicationManifest` is preserved, and `NoWin32Manifest=true` disables the
package-provided manifest. A custom manifest must include `Microsoft.Windows.Common-Controls`
version `6.0.0.0`; without it, Windows may fail to load the native host because the
common-control API set is not activated for the process.

## Links

- [Repository](https://github.com/akeit0/gpui-dotnet)
- [Interactive sample](https://github.com/akeit0/gpui-dotnet/tree/main/samples/Gpui.Sample)
- [Packaging and platform details](https://github.com/akeit0/gpui-dotnet/blob/main/docs/PACKAGING.md)
