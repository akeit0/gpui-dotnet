using System.Reflection;
using Gpui.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Gpui.Tests;

public sealed class GeneratorTests
{
    [Fact]
    public void GeneratesAotFactoryClickDispatchAndListRenderer()
    {
        const string source = """
            using Gpui;

            [GpuiView]
            public sealed partial class GeneratedView : View
            {
                [GpuiEvent]
                private void Save(ClickEvent e) { }

                [GpuiEvent]
                private void SearchChanged(InputEvent e) { }

                [GpuiListItem]
                private Element Row(int index, ref RenderContext ui) => ui.Text($"Row {index}");

                protected override Element Render(ref RenderContext ui) => ui.Div();
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(
            result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        );
        var generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("CreateGpuiView", generated, StringComparison.Ordinal);
        Assert.Contains(
            "Callback<global::Gpui.ClickEvent> Save",
            generated,
            StringComparison.Ordinal
        );
        Assert.Contains("DispatchClickAsync", generated, StringComparison.Ordinal);
        Assert.Contains(
            "Callback<global::Gpui.InputEvent> SearchChanged",
            generated,
            StringComparison.Ordinal
        );
        Assert.Contains("DispatchInputAsync", generated, StringComparison.Ordinal);
        Assert.Contains("ListItemRenderer Row", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAsyncVoidHandlers()
    {
        const string source = """
            using Gpui;

            [GpuiView]
            public sealed partial class InvalidView : View
            {
                [GpuiEvent]
                private async void Save() { await System.Threading.Tasks.Task.Yield(); }

                protected override Element Render(ref RenderContext ui) => ui.Div();
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GPUI004");
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(View).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "GeneratorProbe",
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)
                ),
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GpuiViewGenerator());
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
