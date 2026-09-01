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
        Assert.Contains(
            "IGeneratedViewFactory<GeneratedView>",
            generated,
            StringComparison.Ordinal
        );
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

    [Fact]
    public void ViewsWithoutRowsStillGenerateStableRowDispatch()
    {
        const string source = """
            using Gpui;

            [GpuiView]
            public sealed partial class PlainView : View
            {
                protected override Element Render(ref RenderContext ui) => ui.Div();
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(
            result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        );
        var generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("override global::Gpui.Element RenderListItem", generated);
        Assert.Contains("private GeneratedGpuiRows Rows => new(this);", generated);
        Assert.Contains("private readonly struct GeneratedGpuiRows", generated);
    }

    [Fact]
    public void PropsViewsRequireThePropsChildOverloadAtCompileTime()
    {
        const string source = """
            using Gpui;

            public readonly record struct HeaderProps(string Title);

            [GpuiView]
            public sealed partial class HeaderView : View<HeaderProps>
            {
                protected override Element Render(ref RenderContext ui) => ui.Text(Props.Title);
            }

            [GpuiView]
            public sealed partial class ParentView : View
            {
                protected override Element Render(ref RenderContext ui) => ui.Child<HeaderView>();
            }
            """;

        var (_, output) = RunGeneratorAndUpdateCompilation(source);
        var errors = output
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS0311");
    }

    [Fact]
    public void NoPropsViewsRejectThePropsChildOverloadAtCompileTime()
    {
        const string source = """
            using Gpui;

            public readonly record struct HeaderProps(string Title);

            [GpuiView]
            public sealed partial class PlainView : View
            {
                protected override Element Render(ref RenderContext ui) => ui.Div();
            }

            [GpuiView]
            public sealed partial class ParentView : View
            {
                protected override Element Render(ref RenderContext ui) =>
                    ui.Child<PlainView, HeaderProps>(new("Invalid"));
            }
            """;

        var (_, output) = RunGeneratorAndUpdateCompilation(source);
        var errors = output
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS0311");
    }

    [Fact]
    public void PropsViewsCannotBeWindowRootsAtCompileTime()
    {
        const string source = """
            using Gpui;

            public readonly record struct HeaderProps(string Title);

            [GpuiView]
            public sealed partial class HeaderView : View<HeaderProps>
            {
                protected override Element Render(ref RenderContext ui) => ui.Text(Props.Title);
            }

            public static class Bootstrap
            {
                public static void Open() =>
                    new GpuiApplication().OpenWindow(new HeaderView());
            }
            """;

        var (_, output) = RunGeneratorAndUpdateCompilation(source);
        var errors = output
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS1503");
    }

    [Fact]
    public void PropsViewsUseTheSharedFactoryAndCompileWithRequiredProps()
    {
        const string source = """
            using Gpui;

            public readonly record struct HeaderProps(string Title);

            [GpuiView]
            public sealed partial class HeaderView : View<HeaderProps>
            {
                protected override Element Render(ref RenderContext ui) => ui.Text(Props.Title);
            }

            [GpuiView]
            public sealed partial class ParentView : View
            {
                protected override Element Render(ref RenderContext ui) =>
                    ui.Child<HeaderView, HeaderProps>("header", new("Overview"));
            }
            """;

        var (result, output) = RunGeneratorAndUpdateCompilation(source);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(
            output.GetDiagnostics(TestContext.Current.CancellationToken),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
        );
        Assert.Contains("IGeneratedViewFactory<HeaderView>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NonEquatableStructPropsAreRejectedByTheGenericConstraint()
    {
        const string source = """
            using Gpui;

            public readonly struct HeaderProps
            {
                public HeaderProps(string title) => Title = title;
                public string Title { get; }
            }

            [GpuiView]
            public sealed partial class HeaderView : View<HeaderProps>
            {
                protected override Element Render(ref RenderContext ui) => ui.Text(Props.Title);
            }
            """;

        var (_, output) = RunGeneratorAndUpdateCompilation(source);
        var errors = output
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS0315");
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var (result, _) = RunGeneratorAndUpdateCompilation(source);
        return result;
    }

    private static (
        GeneratorDriverRunResult Result,
        Compilation Output
    ) RunGeneratorAndUpdateCompilation(string source)
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
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }
}
