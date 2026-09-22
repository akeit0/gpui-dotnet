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
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    private static string UpperSnake(string value) => value.ToUpperInvariant();

    private static string XmlDoc(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
