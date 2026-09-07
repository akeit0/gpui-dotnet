using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Gpui.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SynchronousEventAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor SynchronousEvent = new(
        "GPUI018", "GPUI event callbacks must be synchronous",
        "Use a synchronous callback and WorkScope.Start instead of an async handler or a discarded task",
        "Ownership", DiagnosticSeverity.Error, isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(SynchronousEvent);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.ContainingAssembly.Name is not ("Gpui" or "Gpui.Core" or "Gpui.Editor")
            || method.ContainingType.ToDisplayString() == "Gpui.WorkScope")
            return;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type is not INamedTypeSymbol { DelegateInvokeMethod.ReturnsVoid: true })
                continue;
            var callback = Unwrap(argument.Value);
            var invalid = callback is IAnonymousFunctionOperation lambda
                ? lambda.Symbol.IsAsync || ContainsDetachedWork(lambda.Body)
                : callback is IMethodReferenceOperation { Method.IsAsync: true };
            if (invalid)
                context.ReportDiagnostic(Diagnostic.Create(SynchronousEvent, argument.Syntax.GetLocation()));
        }
    }

    private static bool ContainsDetachedWork(IOperation operation)
    {
        // Nested producers passed to WorkScope.Start have their own async contract.
        if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
            return false;
        if (operation is IExpressionStatementOperation statement)
        {
            var expression = Unwrap(statement.Operation);
            if (IsTask(expression.Type)
                || expression is IInvocationOperation { TargetMethod.IsAsync: true })
                return true;
        }
        if (operation is ISimpleAssignmentOperation { Target: IDiscardOperation } assignment
            && IsTask(assignment.Value.Type))
            return true;
        foreach (var child in operation.ChildOperations)
            if (ContainsDetachedWork(child))
                return true;
        return false;
    }

    private static bool IsTask(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            if (current.Name is "Task" or "ValueTask"
                && current.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
                return true;
        return false;
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
