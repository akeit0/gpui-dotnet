using System.Collections.Immutable;
using Gpui.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Gpui.Tests;

public sealed class ViewWorkAnalyzerTests
{
    [Theory]
    [InlineData("StartWork(1, static (value, _) => Task.FromResult(value), value => _value = value);", null)]
    [InlineData("StartWork(1, StaticProducer, value => _value = value);", null)]
    [InlineData("static Task<int> Local(int value, CancellationToken _) => Task.FromResult(value); StartWork(1, Local, value => _value = value);", null)]
    [InlineData("StartWork(complete: value => _value = value, produce: StaticProducer, request: 1);", null)]
    [InlineData("StartWork(1, (value, _) => Task.FromResult(value), value => _value = value);", "GPUI016")]
    [InlineData("StartWork(1, (value, _) => Task.FromResult(_value + value), value => _value = value);", "GPUI016")]
    [InlineData("StartWork(1, InstanceProducer, value => _value = value);", "GPUI016")]
    [InlineData("Func<int, CancellationToken, Task<int>> producer = StaticProducer; StartWork(1, producer, value => _value = value);", "GPUI016")]
    [InlineData("StartWork(1, StaticProducer, async value => { await Task.Yield(); _value = value; });", "GPUI017")]
    [InlineData("StartWork(1, StaticProducer, AsyncCompletion);", "GPUI017")]
    [InlineData("StartWork(1, StaticProducer, value => _value = value, async error => { await Task.Yield(); });", "GPUI017")]
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
                protected override Element Render(ref RenderContext ui) => ui.Text("work");
                private static Task<int> StaticProducer(int value, CancellationToken token) => Task.FromResult(value);
                private Task<int> InstanceProducer(int value, CancellationToken token) => Task.FromResult(_value + value);
                private async void AsyncCompletion(int value) { await Task.Yield(); _value = value; }
                public void Run() { {{body}} }
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>().ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(View).Assembly.Location));
        var compilation = CSharpCompilation.Create("WorkProbe",
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                cancellationToken: TestContext.Current.CancellationToken)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var diagnostics = await compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ViewWorkAnalyzer())
        ).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        if (expected is null)
            Assert.Empty(diagnostics);
        else
            Assert.Equal(expected, Assert.Single(diagnostics).Id);
    }
}
