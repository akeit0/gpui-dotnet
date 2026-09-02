using Gpui;
using Gpui.Editor;
using static Gpui.Units;

var hostName = OperatingSystem.IsWindows()
    ? "gpui_dotnet_editor.dll"
    : OperatingSystem.IsMacOS()
        ? "libgpui_dotnet_editor.dylib"
        : "libgpui_dotnet_editor.so";
var application = new GpuiApplication(
    new NativeRuntimeOptions
    {
        LibraryPath = Path.Combine(AppContext.BaseDirectory, hostName),
        Extensions = [EditorExtension.Requirement],
    }
);
application.OpenWindow(
    new EditorSampleView(),
    new GpuiWindowOptions
    {
        Title = "GPUI.NET Optional Editor",
        Width = 960,
        Height = 680,
    }
);
application.Run();

[GpuiView]
internal sealed partial class EditorSampleView : View
{
    protected override Element Render(ref RenderContext ui) =>
        ui.Editor(
                "main-document",
                new EditorOptions
                {
                    Language = "rust",
                    InitialValue =
                        "fn main() {\n    println!(\"Hello from an optional GPUI.NET editor host\");\n}\n",
                }
            )
            .Width(Percent(100))
            .Height(Percent(100));
}
