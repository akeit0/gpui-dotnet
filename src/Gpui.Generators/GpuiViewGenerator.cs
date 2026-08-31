using System;
using System.Collections.Generic;
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
    private const string ListItemAttribute = "Gpui.GpuiListItemAttribute";

    private static readonly DiagnosticDescriptor MustBePartial = Error(
        "GPUI001",
        "GPUI view must be partial",
        "'{0}' is marked [GpuiView] and must be declared partial"
    );
    private static readonly DiagnosticDescriptor UnsupportedContainer = Error(
        "GPUI002",
        "Unsupported generated view declaration",
        "'{0}' must be a non-generic top-level public or internal class to use generated GPUI view support"
    );
    private static readonly DiagnosticDescriptor InvalidHandler = Error(
        "GPUI003",
        "Invalid GPUI event handler",
        "Handler '{0}' must be an instance, non-generic method with zero parameters or one ClickEvent/InputEvent parameter and return void, Task, or ValueTask"
    );
    private static readonly DiagnosticDescriptor AsyncVoidHandler = Error(
        "GPUI004",
        "async void GPUI handler is not supported",
        "Handler '{0}' is async void; return ValueTask or Task so the runtime can observe completion and exceptions"
    );
    private static readonly DiagnosticDescriptor HandlerCollision = Error(
        "GPUI005",
        "Generated GPUI handler id collision",
        "Handlers '{0}' and '{1}' generated the same id; rename one handler"
    );
    private static readonly DiagnosticDescriptor ReservedEventsMember = Error(
        "GPUI006",
        "Events member is reserved",
        "'{0}' declares a member named 'Events'; [GpuiView] reserves that name for generated event bindings"
    );
    private static readonly DiagnosticDescriptor DuplicateHandlerName = Error(
        "GPUI007",
        "GPUI event handler names must be unique",
        "'{0}' has multiple [GpuiEvent] methods named '{1}'; generated Events accessors require unique method names"
    );
    private static readonly DiagnosticDescriptor MustDeriveFromView = Error(
        "GPUI008",
        "GPUI generated views must derive from View",
        "'{0}' is marked [GpuiView] but does not derive from Gpui.View"
    );
    private static readonly DiagnosticDescriptor ReservedGeneratedHandlerName = Error(
        "GPUI009",
        "GPUI event handler name is reserved",
        "Handler '{0}' uses a name reserved by generated event bindings; rename the handler"
    );
    private static readonly DiagnosticDescriptor ViewMustBeConstructible = Error(
        "GPUI010",
        "GPUI generated view must be constructible",
        "'{0}' must be non-abstract and have a parameterless instance constructor so framework-owned ui.Child<T>() can create it"
    );
    private static readonly DiagnosticDescriptor ReservedFactoryMember = Error(
        "GPUI011",
        "Generated View factory member is reserved",
        "'{0}' declares a member named 'CreateGpuiView'; [GpuiView] reserves that name for framework child activation"
    );
    private static readonly DiagnosticDescriptor InvalidListRenderer = Error(
        "GPUI012",
        "Invalid GPUI list-item renderer",
        "List renderer '{0}' must be an instance, non-generic synchronous method with signature Element Method(int index, ref RenderContext ui)"
    );
    private static readonly DiagnosticDescriptor ReservedRowsMember = Error(
        "GPUI013",
        "Rows member is reserved",
        "'{0}' declares a member named 'Rows'; [GpuiView] reserves that name for generated list renderer bindings"
    );
    private static readonly DiagnosticDescriptor DuplicateListRendererName = Error(
        "GPUI014",
        "GPUI list renderer names must be unique",
        "'{0}' has multiple [GpuiListItem] methods named '{1}'; generated Rows accessors require unique method names"
    );
    private static readonly DiagnosticDescriptor ListRendererCollision = Error(
        "GPUI015",
        "Generated GPUI list renderer id collision",
        "List renderers '{0}' and '{1}' generated the same id; rename one renderer"
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
        if (
            !view.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is ClassDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
            )
        )
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
        var listRenderers = new List<ListRendererModel>();
        foreach (var method in view.GetMembers().OfType<IMethodSymbol>())
        {
            if (HasAttribute(method, EventAttribute))
            {
                if (method.IsAsync && method.ReturnsVoid)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            AsyncVoidHandler,
                            method.Locations.FirstOrDefault(),
                            method.Name
                        )
                    );
                }
                else if (!TryCreateHandler(view, method, out var handler))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            InvalidHandler,
                            method.Locations.FirstOrDefault(),
                            method.Name
                        )
                    );
                }
                else if (IsReservedHandlerAccessorName(method.Name))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            ReservedGeneratedHandlerName,
                            method.Locations.FirstOrDefault(),
                            method.Name
                        )
                    );
                }
                else
                {
                    handlers.Add(handler);
                }
            }

            if (HasAttribute(method, ListItemAttribute))
            {
                if (!TryCreateListRenderer(view, method, out var renderer))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            InvalidListRenderer,
                            method.Locations.FirstOrDefault(),
                            method.Name
                        )
                    );
                }
                else
                {
                    listRenderers.Add(renderer);
                }
            }
        }

        if (handlers.Count != 0 && view.GetMembers("Events").Length != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(ReservedEventsMember, location, view.Name));
            return;
        }
        if (listRenderers.Count != 0 && view.GetMembers("Rows").Length != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(ReservedRowsMember, location, view.Name));
            return;
        }
        if (
            !ValidateUniqueNames(
                context,
                view,
                handlers.Select(h => h.Method),
                DuplicateHandlerName
            )
        )
            return;
        if (
            !ValidateUniqueNames(
                context,
                view,
                listRenderers.Select(h => h.Method),
                DuplicateListRendererName
            )
        )
            return;
        if (!ValidateUniqueIds(context, handlers, HandlerCollision))
            return;
        if (!ValidateUniqueListIds(context, listRenderers))
            return;

        handlers.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        listRenderers.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        var source = BuildSource(view, handlers, listRenderers);
        var hint = Sanitize(view.ToDisplayString()) + ".GpuiView.g.cs";
        context.AddSource(hint, SourceText.From(source, Encoding.UTF8));
    }

    private static bool ValidateUniqueNames(
        SourceProductionContext context,
        INamedTypeSymbol view,
        IEnumerable<IMethodSymbol> methods,
        DiagnosticDescriptor descriptor
    )
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            if (names.Add(method.Name))
                continue;
            context.ReportDiagnostic(
                Diagnostic.Create(
                    descriptor,
                    method.Locations.FirstOrDefault(),
                    view.Name,
                    method.Name
                )
            );
            return false;
        }
        return true;
    }

    private static bool ValidateUniqueIds(
        SourceProductionContext context,
        IEnumerable<HandlerModel> handlers,
        DiagnosticDescriptor descriptor
    )
    {
        var byId = new Dictionary<uint, HandlerModel>();
        foreach (var handler in handlers)
        {
            if (byId.TryGetValue(handler.Id, out var existing))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor,
                        handler.Method.Locations.FirstOrDefault(),
                        existing.Method.Name,
                        handler.Method.Name
                    )
                );
                return false;
            }
            byId.Add(handler.Id, handler);
        }
        return true;
    }

    private static bool ValidateUniqueListIds(
        SourceProductionContext context,
        IEnumerable<ListRendererModel> renderers
    )
    {
        var byId = new Dictionary<uint, ListRendererModel>();
        foreach (var renderer in renderers)
        {
            if (byId.TryGetValue(renderer.Id, out var existing))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        ListRendererCollision,
                        renderer.Method.Locations.FirstOrDefault(),
                        existing.Method.Name,
                        renderer.Method.Name
                    )
                );
                return false;
            }
            byId.Add(renderer.Id, renderer);
        }
        return true;
    }

    private static bool TryCreateHandler(
        INamedTypeSymbol view,
        IMethodSymbol method,
        out HandlerModel handler
    )
    {
        handler = null!;
        if (
            method.IsStatic
            || method.IsGenericMethod
            || method.MethodKind != MethodKind.Ordinary
            || method.Parameters.Length > 1
        )
            return false;
        var eventKind = HandlerEventKind.Click;
        var takesEvent = false;
        if (method.Parameters.Length == 1)
        {
            var parameter = method.Parameters[0];
            if (parameter.RefKind != RefKind.None)
                return false;
            if (IsNamedType(parameter.Type, "Gpui", "ClickEvent"))
            {
                eventKind = HandlerEventKind.Click;
            }
            else if (IsNamedType(parameter.Type, "Gpui", "InputEvent"))
            {
                eventKind = HandlerEventKind.Input;
            }
            else
            {
                return false;
            }
            takesEvent = true;
        }
        var returnKind = GetReturnKind(method.ReturnType);
        if (returnKind == HandlerReturnKind.Unsupported)
            return false;
        var eventSignature =
            eventKind == HandlerEventKind.Input ? "Gpui.InputEvent" : "Gpui.ClickEvent";
        var signature =
            view.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            + "."
            + method.Name
            + (takesEvent ? $"({eventSignature})" : "()")
            + ":"
            + returnKind;
        var id = NonZeroFnv(signature);
        handler = new HandlerModel(method, id, eventKind, takesEvent, returnKind);
        return true;
    }

    private static bool TryCreateListRenderer(
        INamedTypeSymbol view,
        IMethodSymbol method,
        out ListRendererModel renderer
    )
    {
        renderer = null!;
        if (
            method.IsStatic
            || method.IsGenericMethod
            || method.IsAsync
            || method.MethodKind != MethodKind.Ordinary
            || method.Parameters.Length != 2
        )
            return false;
        var index = method.Parameters[0];
        var ui = method.Parameters[1];
        if (index.RefKind != RefKind.None || index.Type.SpecialType != SpecialType.System_Int32)
            return false;
        if (ui.RefKind != RefKind.Ref || !IsNamedType(ui.Type, "Gpui", "RenderContext"))
            return false;
        if (!IsElementType(method.ReturnType))
            return false;
        var signature =
            view.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            + "."
            + method.Name
            + "(System.Int32,ref Gpui.RenderContext):ListItem";
        renderer = new ListRendererModel(method, NonZeroFnv(signature));
        return true;
    }

    private static bool IsElementType(ITypeSymbol type)
    {
        if (IsNamedType(type, "Gpui", "Element"))
            return true;
        return type is INamedTypeSymbol named
            && named.Name == "Element"
            && named.Arity == 1
            && string.Equals(
                named.ContainingNamespace?.ToDisplayString(),
                "Gpui",
                StringComparison.Ordinal
            );
    }

    private static uint NonZeroFnv(string value)
    {
        var id = Fnv1a32(value);
        return id == 0 ? 1u : id;
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
            return HandlerReturnKind.Void;
        if (IsNamedType(type, "System.Threading.Tasks", "ValueTask"))
            return HandlerReturnKind.ValueTask;
        if (IsNamedType(type, "System.Threading.Tasks", "Task"))
            return HandlerReturnKind.Task;
        return HandlerReturnKind.Unsupported;
    }

    private static bool DerivesFromGpuiView(INamedTypeSymbol view)
    {
        for (var current = view.BaseType; current is not null; current = current.BaseType)
        {
            if (IsNamedType(current, "Gpui", "View"))
                return true;
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

    private static string BuildSource(
        INamedTypeSymbol view,
        IReadOnlyList<HandlerModel> handlers,
        IReadOnlyList<ListRendererModel> listRenderers
    )
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

        AppendEvents(builder, viewIdentifier, handlers);
        AppendListRenderers(builder, viewIdentifier, listRenderers);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendEvents(
        StringBuilder builder,
        string viewIdentifier,
        IReadOnlyList<HandlerModel> handlers
    )
    {
        if (handlers.Count == 0)
            return;
        builder.AppendLine();
        builder.AppendLine("    private GeneratedGpuiEvents Events => new(this);");
        builder.AppendLine();
        builder.AppendLine("    private readonly struct GeneratedGpuiEvents");
        builder.AppendLine("    {");
        builder.Append("        private readonly ").Append(viewIdentifier).AppendLine(" _owner;");
        builder.AppendLine();
        builder
            .Append("        internal GeneratedGpuiEvents(")
            .Append(viewIdentifier)
            .AppendLine(" owner) => _owner = owner;");
        builder.AppendLine();
        foreach (var handler in handlers)
        {
            var eventType =
                handler.EventKind == HandlerEventKind.Input
                    ? "global::Gpui.InputEvent"
                    : "global::Gpui.ClickEvent";
            var binder = handler.EventKind == HandlerEventKind.Input ? "BindInput" : "BindClick";
            builder
                .Append("        internal global::Gpui.Callback<")
                .Append(eventType)
                .Append("> ")
                .Append(EscapeIdentifier(handler.Method.Name))
                .Append(" => _owner.")
                .Append(binder)
                .Append("(0x")
                .Append(handler.Id.ToString("X8"))
                .AppendLine("u);");
        }
        builder.AppendLine("    }");
        AppendEventDispatch(
            builder,
            handlers.Where(handler => handler.EventKind == HandlerEventKind.Click).ToArray(),
            HandlerEventKind.Click
        );
        AppendEventDispatch(
            builder,
            handlers.Where(handler => handler.EventKind == HandlerEventKind.Input).ToArray(),
            HandlerEventKind.Input
        );
    }

    private static void AppendEventDispatch(
        StringBuilder builder,
        IReadOnlyList<HandlerModel> handlers,
        HandlerEventKind eventKind
    )
    {
        if (handlers.Count == 0)
            return;
        var isInput = eventKind == HandlerEventKind.Input;
        var eventType = isInput ? "global::Gpui.InputEvent" : "global::Gpui.ClickEvent";
        var eventName = isInput ? "inputEvent" : "clickEvent";
        var dispatchName = isInput ? "DispatchInputAsync" : "DispatchClickAsync";

        builder.AppendLine();
        builder
            .Append("    protected override global::System.Threading.Tasks.ValueTask ")
            .Append(dispatchName)
            .Append("(uint handlerId, ")
            .Append(eventType)
            .Append(' ')
            .Append(eventName)
            .AppendLine(")");
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
                + (handler.TakesEvent ? $"({eventName})" : "()");
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
        builder
            .Append("                return base.")
            .Append(dispatchName)
            .Append("(handlerId, ")
            .Append(eventName)
            .AppendLine(");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void AppendListRenderers(
        StringBuilder builder,
        string viewIdentifier,
        IReadOnlyList<ListRendererModel> renderers
    )
    {
        if (renderers.Count == 0)
            return;
        builder.AppendLine();
        builder.AppendLine("    private GeneratedGpuiRows Rows => new(this);");
        builder.AppendLine();
        builder.AppendLine("    private readonly struct GeneratedGpuiRows");
        builder.AppendLine("    {");
        builder.Append("        private readonly ").Append(viewIdentifier).AppendLine(" _owner;");
        builder.AppendLine();
        builder
            .Append("        internal GeneratedGpuiRows(")
            .Append(viewIdentifier)
            .AppendLine(" owner) => _owner = owner;");
        builder.AppendLine();
        foreach (var renderer in renderers)
        {
            builder
                .Append("        internal global::Gpui.ListItemRenderer ")
                .Append(EscapeIdentifier(renderer.Method.Name))
                .Append(" => _owner.BindListRenderer(0x")
                .Append(renderer.Id.ToString("X8"))
                .AppendLine("u);");
        }
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine(
            "    protected override global::Gpui.Element RenderListItem(uint rendererId, int index, ref global::Gpui.RenderContext ui)"
        );
        builder.AppendLine("    {");
        builder.AppendLine("        switch (rendererId)");
        builder.AppendLine("        {");
        foreach (var renderer in renderers)
        {
            builder
                .Append("            case 0x")
                .Append(renderer.Id.ToString("X8"))
                .AppendLine("u:");
            builder
                .Append("                return ")
                .Append(EscapeIdentifier(renderer.Method.Name))
                .AppendLine("(index, ref ui);");
        }
        builder.AppendLine("            default:");
        builder.AppendLine(
            "                return base.RenderListItem(rendererId, index, ref ui);"
        );
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, "Gpui.Generation", DiagnosticSeverity.Error, true);

    private static string GetAccessibility(Accessibility accessibility) =>
        accessibility == Accessibility.Public ? "public" : "internal";

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
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        return builder.ToString();
    }

    private enum HandlerReturnKind
    {
        Unsupported,
        Void,
        Task,
        ValueTask,
    }

    private enum HandlerEventKind
    {
        Click,
        Input,
    }

    private sealed class HandlerModel
    {
        internal HandlerModel(
            IMethodSymbol method,
            uint id,
            HandlerEventKind eventKind,
            bool takesEvent,
            HandlerReturnKind returnKind
        )
        {
            Method = method;
            Id = id;
            EventKind = eventKind;
            TakesEvent = takesEvent;
            ReturnKind = returnKind;
        }

        internal IMethodSymbol Method { get; }
        internal uint Id { get; }
        internal HandlerEventKind EventKind { get; }
        internal bool TakesEvent { get; }
        internal HandlerReturnKind ReturnKind { get; }
    }

    private sealed class ListRendererModel
    {
        internal ListRendererModel(IMethodSymbol method, uint id)
        {
            Method = method;
            Id = id;
        }

        internal IMethodSymbol Method { get; }
        internal uint Id { get; }
    }
}
