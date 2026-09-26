internal static partial class BindingGenerator
{
    private static ulong CapabilityBit(BindingSchema schema, string capability)
    {
        var index = schema.Capabilities.FindIndex(value => value == capability);
        if (index < 0)
        {
            throw new InvalidOperationException($"Unknown capability '{capability}'.");
        }
        return 1UL << index;
    }

    private static ulong CapabilityMask(BindingSchema schema, IEnumerable<string> capabilities)
    {
        ulong result = 0;
        foreach (var capability in capabilities)
        {
            result |= CapabilityBit(schema, capability);
        }
        return result;
    }

    private static string CapabilityInterface(string capability) =>
        $"I{Pascal(capability)}ElementTag";

    private static string Pascal(string value) =>
        string.Concat(
            value
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
        );

    private static string Camel(string value)
    {
        var pascal = Pascal(value);
        var camel = char.ToLowerInvariant(pascal[0]) + pascal[1..];
        return camel switch
        {
            "abstract"
            or "as"
            or "base"
            or "bool"
            or "break"
            or "byte"
            or "case"
            or "catch"
            or "char"
            or "checked"
            or "class"
            or "const"
            or "continue"
            or "decimal"
            or "default"
            or "delegate"
            or "do"
            or "double"
            or "else"
            or "enum"
            or "event"
            or "explicit"
            or "extern"
            or "false"
            or "finally"
            or "fixed"
            or "float"
            or "for"
            or "foreach"
            or "goto"
            or "if"
            or "implicit"
            or "in"
            or "int"
            or "interface"
            or "internal"
            or "is"
            or "lock"
            or "long"
            or "namespace"
            or "new"
            or "null"
            or "object"
            or "operator"
            or "out"
            or "override"
            or "params"
            or "private"
            or "protected"
            or "public"
            or "readonly"
            or "ref"
            or "return"
            or "sbyte"
            or "sealed"
            or "short"
            or "sizeof"
            or "stackalloc"
            or "static"
            or "string"
            or "struct"
            or "switch"
            or "this"
            or "throw"
            or "true"
            or "try"
            or "typeof"
            or "uint"
            or "ulong"
            or "unchecked"
            or "unsafe"
            or "ushort"
            or "using"
            or "virtual"
            or "void"
            or "volatile"
            or "while" => $"@{camel}",
            _ => camel,
        };
    }

    private static string UpperSnake(string value) => value.ToUpperInvariant();

    private static string XmlDoc(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
