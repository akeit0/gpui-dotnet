using System.Text;

internal static partial class BindingGenerator
{
    private static string GenerateIdReference(BindingSchema schema)
    {
        var builder = new StringBuilder(
            "<!-- Generated from bindings/schema.json. Do not edit. -->\n\n# Semantic IDs\n\n"
        );
        builder.AppendLine(
            "The schema owns these IDs. See [Binding generation](BINDING_GENERATION.md) for allocation rules and [ABI](ABI.md) for payload contracts."
        );
        builder.AppendLine("\n## Components\n\n| ID | Component |\n| --- | --- |");
        foreach (var component in schema.Components)
            builder.AppendLine($"| {component.Id} | `{component.CSharp}` |");
        builder.AppendLine("\n## Operations\n\n| Group | Allocated range |\n| --- | --- |");
        foreach (var group in schema.OperationGroups)
            builder.AppendLine(
                $"| [{group.Name}](#{group.Name}) | {group.FirstId}–{group.LastId} |"
            );
        foreach (var group in schema.OperationGroups)
        {
            builder.AppendLine(
                $"\n### {group.Name}\n\n| ID | Operation | Capability |\n| --- | --- | --- |"
            );
            foreach (var op in group.Operations)
                builder.AppendLine($"| {op.Id} | `{op.CSharp}` | `{op.Requires}` |");
        }
        builder.AppendLine(
            "\n## Resource commands\n\n| Resource ID | Resource | Command ID | Command |\n| --- | --- | --- | --- |"
        );
        foreach (var resource in schema.Resources)
        foreach (var command in resource.Commands)
            builder.AppendLine(
                $"| {resource.Id} | `{resource.CSharp}` | {command.Id} | `{command.CSharp}` |"
            );
        builder.AppendLine("\n## Control events\n\n| ID | Family | Event |\n| --- | --- | --- |");
        foreach (var evt in schema.ControlEvents)
            builder.AppendLine($"| {evt.Id} | `{evt.Group}` | `{evt.CSharp}` |");
        return builder.ToString();
    }
}
