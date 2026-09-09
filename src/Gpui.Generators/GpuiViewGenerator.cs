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
    private static readonly DiagnosticDescriptor MustDeriveFromView = Error(
        "GPUI008",
        "GPUI generated views must derive from View",
        "'{0}' is marked [GpuiView] but does not derive from Gpui.View or Gpui.View<TProps>"
    );
    private static readonly DiagnosticDescriptor ViewMustBeConstructible = Error(
        "GPUI010",
        "GPUI generated view must be constructible",
        "'{0}' must be non-abstract and have a constructor accepting ViewConstruction followed by initial props for a props View"
    );
    private static readonly DiagnosticDescriptor ReservedFactoryMember = Error(
        "GPUI011",
        "Generated View factory member is reserved",
        "'{0}' declares 'CreateGpuiView' or 'Spec'; [GpuiView] reserves those names for framework-owned declarations and construction"
    );
    private static readonly DiagnosticDescriptor InvalidListRenderer = Error(
        "GPUI012",
        "Invalid GPUI list-item renderer",
        "List renderer '{0}' must be an instance, non-generic synchronous method with signature {1}"
    );
    private static readonly DiagnosticDescriptor ReservedItemsMember = Error(
        "GPUI013",
        "Items member is reserved",
        "'{0}' declares a member named 'Items'; [GpuiView] reserves that name for generated list renderer bindings"
    );
    private static readonly DiagnosticDescriptor DuplicateListRendererName = Error(
        "GPUI014",
        "GPUI list renderer names must be unique",
        "'{0}' has multiple [GpuiListItem] methods named '{1}'; generated Items accessors require unique method names"
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
        var propsType = GetPropsType(view);
        if (
            view.IsAbstract
            || (
                !view.InstanceConstructors.All(static c => c.IsImplicitlyDeclared)
                && !view.InstanceConstructors.Any(c =>
                    c.Parameters.Length == (propsType is null ? 1 : 2)
                    && IsNamedType(c.Parameters[0].Type, "Gpui", "ViewConstruction")
                    && c.Parameters[0].RefKind == RefKind.None
                    && (
                        propsType is null
                        || (
                            SymbolEqualityComparer.Default.Equals(c.Parameters[1].Type, propsType)
                            && c.Parameters[1].RefKind == RefKind.None
                        )
                    )
                )
            )
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(ViewMustBeConstructible, location, view.Name)
            );
            return;
        }
        if (view.GetMembers("CreateGpuiView").Length != 0 || view.GetMembers("Spec").Length != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(ReservedFactoryMember, location, view.Name));
            return;
        }

        var listRenderers = new List<ListRendererModel>();
        foreach (var method in view.GetMembers().OfType<IMethodSymbol>())
        {
            if (HasAttribute(method, ListItemAttribute))
            {
                if (!TryCreateListRenderer(view, method, out var renderer))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            InvalidListRenderer,
                            method.Locations.FirstOrDefault(),
                            method.Name,
                            propsType is null
                                ? "Element Method(int index, ref RenderContext ui)"
                                : $"Element Method(int index, in {propsType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} props, ref RenderContext ui)"
                        )
                    );
                }
                else
                {
                    listRenderers.Add(renderer);
                }
            }
        }

        if (view.GetMembers("Items").Length != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(ReservedItemsMember, location, view.Name));
            return;
        }
        if (
            !ValidateUniqueNames(
                context,
                view,
                listRenderers.Select(h => h.Method),
                DuplicateListRendererName
            )
        )
            return;
        if (!ValidateUniqueListIds(context, listRenderers))
            return;

        listRenderers.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        var source = BuildSource(view, listRenderers);
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
            || method.Parameters.Length != (GetPropsType(view) is null ? 2 : 3)
        )
            return false;
        var index = method.Parameters[0];
        var props = GetPropsType(view);
        var ui = method.Parameters[method.Parameters.Length - 1];
        if (
            props is not null
            && (
                method.Parameters[1].RefKind != RefKind.In
                || !SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, props)
            )
        )
            return false;
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

    private static ITypeSymbol? GetPropsType(INamedTypeSymbol view)
    {
        for (var current = view.BaseType; current is not null; current = current.BaseType)
            if (
                current.Name == "View"
                && current.Arity == 1
                && current.ContainingNamespace?.ToDisplayString() == "Gpui"
            )
                return current.TypeArguments[0];
        return null;
    }

    private static bool DerivesFromGpuiView(INamedTypeSymbol view)
    {
        for (var current = view.BaseType; current is not null; current = current.BaseType)
        {
            if (
                current.Name == "View"
                && current.Arity is 0 or 1
                && string.Equals(
                    current.ContainingNamespace?.ToDisplayString(),
                    "Gpui",
                    StringComparison.Ordinal
                )
            )
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
        var props = GetPropsType(view)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var typeArguments = viewIdentifier + (props is null ? "" : ", " + props);
        var parameters =
            "global::Gpui.ViewConstruction construction"
            + (props is null ? "" : ", " + props + " initialProps");
        builder
            .Append(GetAccessibility(view.DeclaredAccessibility))
            .Append(" partial class ")
            .Append(viewIdentifier)
            .Append(" : global::Gpui.IGeneratedViewFactory<")
            .Append(typeArguments)
            .AppendLine(">");
        builder.AppendLine("{");
        if (view.InstanceConstructors.All(static c => c.IsImplicitlyDeclared))
            builder
                .Append("    public ")
                .Append(viewIdentifier)
                .Append("(")
                .Append(parameters)
                .AppendLine(") : base(construction) { }");
        builder.AppendLine(
            "    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        builder
            .Append("    public static ")
            .Append(viewIdentifier)
            .Append(" CreateGpuiView(")
            .Append(parameters)
            .Append(") => new ")
            .Append(viewIdentifier)
            .Append("(construction")
            .Append(props is null ? "" : ", initialProps")
            .AppendLine(");");
        builder
            .Append("    public static global::Gpui.ViewSpec<")
            .Append(typeArguments)
            .Append("> Spec(")
            .Append(props is null ? "" : props + " props")
            .Append(") => ")
            .AppendLine(props is null ? "default;" : "new(props);");

        AppendListRenderers(builder, viewIdentifier, listRenderers, props);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendListRenderers(
        StringBuilder builder,
        string viewIdentifier,
        IReadOnlyList<ListRendererModel> renderers,
        string? props
    )
    {
        builder.AppendLine();
        builder.AppendLine("    private GeneratedGpuiItems Items => new(this);");
        builder.AppendLine();
        builder.AppendLine("    private readonly struct GeneratedGpuiItems");
        builder.AppendLine("    {");
        builder.Append("        private readonly ").Append(viewIdentifier).AppendLine(" _owner;");
        builder.AppendLine();
        builder
            .Append("        internal GeneratedGpuiItems(")
            .Append(viewIdentifier)
            .AppendLine(" owner) => _owner = owner;");
        if (renderers.Count != 0)
        {
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
                .AppendLine(
                    props is null ? "(index, ref ui);" : "(index, in CommittedProps, ref ui);"
                );
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
        // Escape the escape marker too: A.B_C and A_B.C must not share a hint name.
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
            else
                builder
                    .Append('_')
                    .Append(
                        ((int)character).ToString(
                            "X4",
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    );
        }
        return builder.ToString();
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
