using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Gpui.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ViewWorkAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor StaticProducer = new(
        "GPUI016", "Work producers must be static",
        "Pass a static lambda or static method directly as the work producer; put inputs in the request snapshot",
        "Ownership", DiagnosticSeverity.Error, isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor SynchronousCompletion = new(
        "GPUI017", "Work completion handlers must be synchronous",
        "Work completion handlers cannot be async; start another View-owned operation instead",
        "Ownership", DiagnosticSeverity.Error, isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(StaticProducer, SynchronousCompletion);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod.Name != "Start"
            || invocation.TargetMethod.ContainingType.ToDisplayString() != "Gpui.WorkScope")
            return;

        foreach (var argument in invocation.Arguments)
        {
            var value = Unwrap(argument.Value);
            if (argument.Parameter?.Name == "produce")
            {
                var isStatic = value is IAnonymousFunctionOperation lambda
                    ? lambda.Syntax is LambdaExpressionSyntax syntax
                        && syntax.Modifiers.Any(SyntaxKind.StaticKeyword)
                    : value is IMethodReferenceOperation method
                        && method.Method.IsStatic && method.Instance is null;
                if (!isStatic)
                    context.ReportDiagnostic(Diagnostic.Create(StaticProducer, argument.Syntax.GetLocation()));
            }
            else if (argument.Parameter?.Name is "complete" or "failed" or "cancelled")
            {
                if (value is IAnonymousFunctionOperation { Symbol.IsAsync: true }
                    or IMethodReferenceOperation { Method.IsAsync: true })
                    context.ReportDiagnostic(Diagnostic.Create(SynchronousCompletion, argument.Syntax.GetLocation()));
            }
        }
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    break;
                case IDelegateCreationOperation creation:
                    operation = creation.Target;
                    break;
                default:
                    return operation;
            }
        }
    }
}
