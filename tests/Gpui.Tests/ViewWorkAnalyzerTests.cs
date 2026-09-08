using System.Collections.Immutable;
using Gpui.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Gpui.Tests;

public sealed class ViewWorkAnalyzerTests
{
    [Theory]
    [InlineData(
        "_work.StartLatest(this, 1, StaticProducer, static (view, value) => view._value = value);",
        null
    )]
    [InlineData(
        "_work.StartLatest(this, 1, InstanceProducer, static (view, value) => view._value = value);",
        "GPUI016"
    )]
    [InlineData(
        "_work.StartLatest(this, 1, StaticProducer, static async (view, value) => { await Task.Yield(); view._value = value; });",
        "GPUI017"
    )]
    [InlineData(
        "_work.Start(this, 1, static (value, _) => Task.FromResult(value), (_, value) => _value = value);",
        null
    )]
    [InlineData("_work.Start(this, 1, StaticProducer, (_, value) => _value = value);", null)]
    [InlineData(
        "static Task<int> Local(int value, CancellationToken _) => Task.FromResult(value); _work.Start(this, 1, Local, (_, value) => _value = value);",
        null
    )]
    [InlineData(
        "_work.Start(state: this, complete: (_, value) => _value = value, produce: StaticProducer, request: 1);",
        null
    )]
    [InlineData(
        "_work.Start(this, 1, (value, _) => Task.FromResult(value), (_, value) => _value = value);",
        "GPUI016"
    )]
    [InlineData(
        "_work.Start(this, 1, (value, _) => Task.FromResult(_value + value), (_, value) => _value = value);",
        "GPUI016"
    )]
    [InlineData("_work.Start(this, 1, InstanceProducer, (_, value) => _value = value);", "GPUI016")]
    [InlineData(
        "Func<int, CancellationToken, Task<int>> producer = StaticProducer; _work.Start(this, 1, producer, (_, value) => _value = value);",
        "GPUI016"
    )]
    [InlineData(
        "_work.Start(this, 1, StaticProducer, async (_, value) => { await Task.Yield(); _value = value; });",
        "GPUI017"
    )]
    [InlineData("_work.Start(this, 1, StaticProducer, AsyncCompletion);", "GPUI017")]
    [InlineData(
        "_work.Start(this, 1, StaticProducer, (_, value) => _value = value, async (_, error) => { await Task.Yield(); });",
        "GPUI017"
    )]
    [InlineData(
        "_work.Start(this, 1, StaticProducer, static (view, value) => view._value = value);",
        null
    )]
    [InlineData(
        "_work.Start(this, 1, InstanceProducer, static (view, value) => view._value = value);",
        "GPUI016"
    )]
    [InlineData(
        "_work.Start(this, 1, StaticProducer, static async (view, value) => { await Task.Yield(); view._value = value; });",
        "GPUI017"
    )]
    [InlineData(
        "_work.Start(this, 1, StaticProducer, static (view, value) => view._value = value, static async (view, error) => { await Task.Yield(); });",
        "GPUI017"
    )]
    [InlineData(
        "_work.Start(this, 1, StaticProducer, static (view, value) => view._value = value, cancelled: static async view => { await Task.Yield(); });",
        "GPUI017"
    )]
    [InlineData(
        "_work.Start(this, 1, StaticProducer, static (view, value) => view._value = value, cancelled: static view => view._value = 0);",
        null
    )]
    public async Task EnforcesProducerAndCompletionBoundaries(string body, string? expected)
    {
        var source = $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Gpui;
            public sealed class WorkView : View
            {
                private int _value;
                private WorkScope _work = null!;
                public WorkView(ViewConstruction context) : base(context) => _work = context.Work;
                protected override Element Render(ref RenderContext ui) => ui.Text("work");
                private static Task<int> StaticProducer(int value, CancellationToken token) => Task.FromResult(value);
                private Task<int> InstanceProducer(int value, CancellationToken token) => Task.FromResult(_value + value);
                private async void AsyncCompletion(WorkView view, int value) { await Task.Yield(); _value = value; }
                public void Run() { {{body}} }
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(View).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "WorkProbe",
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
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ViewWorkAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        if (expected is null)
            Assert.Empty(diagnostics);
        else
            Assert.Equal(expected, Assert.Single(diagnostics).Id);
    }
}
