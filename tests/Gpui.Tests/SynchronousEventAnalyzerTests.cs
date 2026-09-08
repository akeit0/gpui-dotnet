using System.Collections.Immutable;
using Gpui.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Gpui.Tests;

public sealed class SynchronousEventAnalyzerTests
{
    [Theory]
    [InlineData("GpuiMenuItem.Command(\"Save\", async () => { await Task.Yield(); });", true)]
    [InlineData("Dispatcher.Post(async () => { await Task.Yield(); });", true)]
    [InlineData("Dispatcher.Post(this, async view => { await Task.Yield(); });", true)]
    [InlineData(
        "Action callback = async () => { await Task.Yield(); }; GpuiMenuItem.Command(\"Save\", callback);",
        false
    )]
    [InlineData(
        "ui.Button(\"b\", \"B\").OnClick(this, async (view, e) => { await Task.Yield(); });",
        true
    )]
    [InlineData("ui.Button(\"b\", \"B\").OnClick(this, AsyncClick);", true)]
    [InlineData("ui.Button(\"b\", \"B\").OnClick(this, (view, e) => GetData());", true)]
    [InlineData("ui.Button(\"b\", \"B\").OnClick(this, (view, e) => GetValueData());", true)]
    [InlineData("ui.Button(\"b\", \"B\").OnClick(this, (view, e) => { _ = GetData(); });", true)]
    [InlineData("ui.Button(\"b\", \"B\").OnClick(this, (view, e) => AsyncClick(view, e));", true)]
    [InlineData(
        "ui.BindNativeExtensionEvent<EventView, ExtensionEvent>(this, async (view, e) => { await Task.Yield(); });",
        true
    )]
    [InlineData("ui.Button(\"b\", \"B\").OnClick(this, (view, e) => view._value++);", false)]
    [InlineData(
        "ui.Button(\"b\", \"B\").OnClick(this, (view, e) => view._work.Start(view, 1, static async (value, token) => { await Task.Yield(); return value; }, static (owner, value) => owner._value = value));",
        false
    )]
    [InlineData("AcceptOrdinaryCallback(async () => { await Task.Yield(); });", false)]
    public async Task DetectsAsyncCallbacksWithoutRejectingOwnedProducers(
        string body,
        bool rejected
    )
    {
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using Gpui;
            public sealed class EventView : View
            {
                private int _value;
                private WorkScope _work = null!;
                public EventView(ViewConstruction context) : base(context) => _work = context.Work;
                private static async void AsyncClick(EventView view, ClickEvent e) { await Task.Yield(); }
                private static Task<int> GetData() => Task.FromResult(1);
                private static ValueTask<int> GetValueData() => ValueTask.FromResult(1);
                private static void AcceptOrdinaryCallback(Action callback) { }
                protected override Element Render(ref RenderContext ui)
                {
                    {{body}}
                    return ui.Div();
                }
            }
            public sealed class ExtensionEvent : INativeExtensionEvent<ExtensionEvent>
            {
                public static ExtensionEvent Decode(NativeExtensionEvent value) => new();
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(View).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "EventProbe",
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                    cancellationToken: TestContext.Current.CancellationToken
                ),
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        );
        var diagnostics = await compilation
            .WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new SynchronousEventAnalyzer())
            )
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        if (rejected)
            Assert.Equal("GPUI018", Assert.Single(diagnostics).Id);
        else
            Assert.Empty(diagnostics);
    }
}
