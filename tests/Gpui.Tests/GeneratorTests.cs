using System.Reflection;
using Gpui.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Gpui.Tests;

public sealed class GeneratorTests
{
    [Fact]
    public void GeneratesAotFactoryAndListRendererWithoutEventDispatch()
    {
        const string source = """
            using Gpui;

            [GpuiView]
            public sealed partial class GeneratedView : View
            {
                private void Save(ClickEvent e) { }

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
        Assert.DoesNotContain("GeneratedGpuiEvents", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatchClickAsync", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatchInputAsync", generated, StringComparison.Ordinal);
        Assert.Contains("ListItemRenderer Row", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryMethodsDoNotBecomeEventBindings()
    {
        const string source = """
            using Gpui;

            [GpuiView]
            public sealed partial class InvalidView : View
            {
                private async void Save() { await System.Threading.Tasks.Task.Yield(); }

                protected override Element Render(ref RenderContext ui) => ui.Div();
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(
            result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        );
        var generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.DoesNotContain("Save", generated, StringComparison.Ordinal);
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
