using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Gpui.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class GpuiViewGenerator : IIncrementalGenerator
{
    private const string ViewAttribute = "Gpui.GpuiViewAttribute";
    private const string EventAttribute = "Gpui.GpuiEventAttribute";

    private static readonly DiagnosticDescriptor MustBePartial = new(
        "GPUIV001",
        "GPUI view must be partial",
        "'{0}' is marked [GpuiView] and must be declared partial",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor UnsupportedContainer = new(
        "GPUIV002",
        "Unsupported generated view declaration",
        "'{0}' must be a non-generic top-level class to use generated GPUI view support",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor InvalidHandler = new(
        "GPUIV003",
        "Invalid GPUI event handler",
        "Handler '{0}' must be an instance, non-generic method with zero parameters or one ClickEvent parameter and return void, Task, or ValueTask",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor AsyncVoidHandler = new(
        "GPUIV004",
        "async void GPUI handler is not supported",
        "Handler '{0}' is async void; return ValueTask or Task so the runtime can observe completion and exceptions",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor HandlerCollision = new(
        "GPUIV005",
        "Generated GPUI handler id collision",
        "Handlers '{0}' and '{1}' generated the same id; rename one handler",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor ReservedEventsMember = new(
        "GPUIV006",
        "Events member is reserved",
        "'{0}' declares a member named 'Events'; [GpuiView] reserves that name for generated event bindings",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor DuplicateHandlerName = new(
        "GPUIV007",
        "GPUI event handler names must be unique",
        "'{0}' has multiple [GpuiEvent] methods named '{1}'; generated Events accessors require unique method names",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor MustDeriveFromView = new(
        "GPUIV008",
        "GPUI generated views must derive from View",
        "'{0}' is marked [GpuiView] but does not derive from Gpui.View",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor ReservedGeneratedHandlerName = new(
        "GPUIV009",
        "GPUI event handler name is reserved",
        "Handler '{0}' uses a name reserved by generated event bindings; rename the handler",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor ViewMustBeConstructible = new(
        "GPUIV010",
        "GPUI generated view must be constructible",
        "'{0}' must be non-abstract and have a parameterless instance constructor so framework-owned ui.Child<T>() can create it",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor ReservedFactoryMember = new(
        "GPUIV011",
        "Generated View factory member is reserved",
        "'{0}' declares a member named 'CreateGpuiView'; [GpuiView] reserves that name for framework child activation",
        "Gpui.Generation",
        DiagnosticSeverity.Error,
        true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var views = context.SyntaxProvider.ForAttributeWithMetadataName(
            ViewAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol
        );

        context.RegisterSourceOutput(
            views,
            static (productionContext, view) => Generate(productionContext, view)
        );
    }

    private static void Generate(SourceProductionContext context, INamedTypeSymbol view)
    {
        var location = view.Locations.FirstOrDefault();
        var isPartial = view.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is ClassDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
        );
        if (!isPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(MustBePartial, location, view.Name));
            return;
        }

        if (
            view.ContainingType is not null
            || view.TypeParameters.Length != 0
            || view.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
        )
        {
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedContainer, location, view.Name));
            return;
        }

        if (!DerivesFromGpuiView(view))
        {
            context.ReportDiagnostic(Diagnostic.Create(MustDeriveFromView, location, view.Name));
            return;
        }

        if (
            view.IsAbstract
            || !view.InstanceConstructors.Any(static constructor =>
                constructor.Parameters.Length == 0
            )
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(ViewMustBeConstructible, location, view.Name)
            );
            return;
        }

        if (view.GetMembers("CreateGpuiView").Length != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(ReservedFactoryMember, location, view.Name));
            return;
        }

        var handlers = new List<HandlerModel>();
        foreach (var method in view.GetMembers().OfType<IMethodSymbol>())
        {
            if (!HasAttribute(method, EventAttribute))
            {
                continue;
            }

            if (method.IsAsync && method.ReturnsVoid)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        AsyncVoidHandler,
                        method.Locations.FirstOrDefault(),
                        method.Name
                    )
                );
                continue;
            }

            if (!TryCreateHandler(view, method, out var handler))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InvalidHandler,
                        method.Locations.FirstOrDefault(),
                        method.Name
                    )
                );
                continue;
            }

            if (IsReservedHandlerAccessorName(method.Name))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        ReservedGeneratedHandlerName,
                        method.Locations.FirstOrDefault(),
                        method.Name
                    )
                );
                continue;
            }

            handlers.Add(handler);
        }

        if (handlers.Count != 0 && view.GetMembers("Events").Length != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(ReservedEventsMember, location, view.Name));
            return;
        }

        var handlerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (!handlerNames.Add(handler.Method.Name))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DuplicateHandlerName,
                        handler.Method.Locations.FirstOrDefault(),
                        view.Name,
                        handler.Method.Name
                    )
                );
                return;
            }
        }

        var byId = new Dictionary<uint, HandlerModel>();
        foreach (var handler in handlers)
        {
            if (byId.TryGetValue(handler.Id, out var existing))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        HandlerCollision,
                        view.Locations.FirstOrDefault(),
                        existing.Method.Name,
                        handler.Method.Name
                    )
                );
                return;
            }
            byId.Add(handler.Id, handler);
        }

        handlers.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        var source = BuildSource(view, handlers);
        var hint = Sanitize(view.ToDisplayString()) + ".GpuiView.g.cs";
        context.AddSource(hint, SourceText.From(source, Encoding.UTF8));
    }

    private static bool TryCreateHandler(
        INamedTypeSymbol view,
        IMethodSymbol method,
        out HandlerModel handler
    )
    {
        handler = null!;
        if (method.IsStatic || method.IsGenericMethod || method.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }

        if (method.Parameters.Length > 1)
        {
            return false;
        }

        var takesClick = false;
        if (method.Parameters.Length == 1)
        {
            var parameter = method.Parameters[0];
            if (
                parameter.RefKind != RefKind.None
                || !IsNamedType(parameter.Type, "Gpui", "ClickEvent")
            )
            {
                return false;
            }
            takesClick = true;
        }

        var returnKind = GetReturnKind(method.ReturnType);
        if (returnKind == HandlerReturnKind.Unsupported)
        {
            return false;
        }

        var signature =
            view.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            + "."
            + method.Name
            + (takesClick ? "(Gpui.ClickEvent)" : "()")
            + ":"
            + returnKind;
        var id = Fnv1a32(signature);
        if (id == 0)
        {
            id = 1;
        }

        handler = new HandlerModel(method, id, takesClick, returnKind);
        return true;
    }

    private static bool IsReservedHandlerAccessorName(string name) =>
        name
            is "GeneratedGpuiEvents"
                or "_owner"
                or "Equals"
                or "GetHashCode"
                or "GetType"
                or "ToString";

    private static HandlerReturnKind GetReturnKind(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Void)
        {
            return HandlerReturnKind.Void;
        }

        if (IsNamedType(type, "System.Threading.Tasks", "ValueTask"))
        {
            return HandlerReturnKind.ValueTask;
        }
        if (IsNamedType(type, "System.Threading.Tasks", "Task"))
        {
            return HandlerReturnKind.Task;
        }
        return HandlerReturnKind.Unsupported;
    }

    private static bool DerivesFromGpuiView(INamedTypeSymbol view)
    {
        for (var current = view.BaseType; current is not null; current = current.BaseType)
        {
            if (IsNamedType(current, "Gpui", "View"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsNamedType(ITypeSymbol type, string @namespace, string name) =>
        type is INamedTypeSymbol named
        && named.Arity == 0
        && string.Equals(named.Name, name, StringComparison.Ordinal)
        && string.Equals(
            named.ContainingNamespace?.ToDisplayString(),
            @namespace,
            StringComparison.Ordinal
        );

    private static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol
            .GetAttributes()
            .Any(attribute =>
                string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    metadataName,
                    StringComparison.Ordinal
                )
            );

    private static string BuildSource(INamedTypeSymbol view, IReadOnlyList<HandlerModel> handlers)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");

        if (!view.ContainingNamespace.IsGlobalNamespace)
        {
            builder
                .Append("namespace ")
                .Append(view.ContainingNamespace.ToDisplayString())
                .AppendLine(";");
            builder.AppendLine();
        }

        var viewIdentifier = EscapeIdentifier(view.Name);
        builder
            .Append(GetAccessibility(view.DeclaredAccessibility))
            .Append(" partial class ")
            .Append(viewIdentifier)
            .Append(" : global::Gpui.IGeneratedViewFactory<")
            .Append(viewIdentifier)
            .AppendLine(">");
        builder.AppendLine("{");
        builder.AppendLine(
            "    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        builder
            .Append("    public static ")
            .Append(viewIdentifier)
            .Append(" CreateGpuiView() => new ")
            .Append(viewIdentifier)
            .AppendLine("();");

        if (handlers.Count != 0)
        {
            builder.AppendLine();
            builder.AppendLine("    private GeneratedGpuiEvents Events => new(this);");
            builder.AppendLine();
            builder.AppendLine("    private readonly struct GeneratedGpuiEvents");
            builder.AppendLine("    {");
            builder
                .Append("        private readonly ")
                .Append(viewIdentifier)
                .AppendLine(" _owner;");
            builder.AppendLine();
            builder
                .Append("        internal GeneratedGpuiEvents(")
                .Append(viewIdentifier)
                .AppendLine(" owner) => _owner = owner;");
            builder.AppendLine();
            foreach (var handler in handlers)
            {
                builder
                    .Append("        internal global::Gpui.Callback<global::Gpui.ClickEvent> ")
                    .Append(EscapeIdentifier(handler.Method.Name))
                    .Append(" => _owner.BindClick(0x")
                    .Append(handler.Id.ToString("X8"))
                    .AppendLine("u);");
            }
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine(
                "    protected override global::System.Threading.Tasks.ValueTask DispatchClickAsync(uint handlerId, global::Gpui.ClickEvent clickEvent)"
            );
            builder.AppendLine("    {");
            builder.AppendLine("        switch (handlerId)");
            builder.AppendLine("        {");
            foreach (var handler in handlers)
            {
                builder
                    .Append("            case 0x")
                    .Append(handler.Id.ToString("X8"))
                    .AppendLine("u:");
                var call =
                    EscapeIdentifier(handler.Method.Name)
                    + (handler.TakesClickEvent ? "(clickEvent)" : "()");
                switch (handler.ReturnKind)
                {
                    case HandlerReturnKind.Void:
                        builder.Append("                ").Append(call).AppendLine(";");
                        builder.AppendLine(
                            "                return global::System.Threading.Tasks.ValueTask.CompletedTask;"
                        );
                        break;
                    case HandlerReturnKind.Task:
                        builder
                            .Append(
                                "                return new global::System.Threading.Tasks.ValueTask("
                            )
                            .Append(call)
                            .AppendLine(");");
                        break;
                    case HandlerReturnKind.ValueTask:
                        builder.Append("                return ").Append(call).AppendLine(";");
                        break;
                }
            }
            builder.AppendLine("            default:");
            builder.AppendLine(
                "                return base.DispatchClickAsync(handlerId, clickEvent);"
            );
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GetAccessibility(Accessibility accessibility)
    {
        switch (accessibility)
        {
            case Accessibility.Public:
                return "public";
            case Accessibility.Internal:
                return "internal";
            default:
                return "internal";
        }
    }

    private static uint Fnv1a32(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        var bytes = Encoding.UTF8.GetBytes(value);
        for (var index = 0; index < bytes.Length; index++)
        {
            hash ^= bytes[index];
            hash *= prime;
        }
        return hash;
    }

    private static string EscapeIdentifier(string value) =>
        SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
            ? "@" + value
            : value;

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }
        return builder.ToString();
    }

    private enum HandlerReturnKind
    {
        Unsupported,
        Void,
        Task,
        ValueTask,
    }

    private sealed class HandlerModel
    {
        internal HandlerModel(
            IMethodSymbol method,
            uint id,
            bool takesClickEvent,
            HandlerReturnKind returnKind
        )
        {
            Method = method;
            Id = id;
            TakesClickEvent = takesClickEvent;
            ReturnKind = returnKind;
        }

        internal IMethodSymbol Method { get; }
        internal uint Id { get; }
        internal bool TakesClickEvent { get; }
        internal HandlerReturnKind ReturnKind { get; }
    }
}
