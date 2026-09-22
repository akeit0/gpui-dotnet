using System.Text.Json.Serialization;

internal sealed record BindingSchema(
    int SchemaVersion,
    List<string> Capabilities,
    List<EnumSchema> Enums,
    List<Component> Components,
    List<OperationGroup> OperationGroups,
    List<LengthMethod> LengthMethods,
    List<ResourceSchema> Resources,
    List<ControlEventSchema> ControlEvents
)
{
    [JsonIgnore]
    public List<Operation> Operations { get; } =
        OperationGroups.SelectMany(group => group.Operations).ToList();
}

internal sealed record OperationGroup(
    string Name,
    int FirstId,
    int LastId,
    List<Operation> Operations
);

internal sealed record ExtensionManifest(List<ExtensionGeneration> Schemas);

internal sealed record ExtensionGeneration(
    string Schema,
    [property: JsonPropertyName("csharpOutput")] string CSharpOutput,
    string RustOutput,
    [property: JsonPropertyName("csharpNamespace")] string CSharpNamespace,
    [property: JsonPropertyName("csharpClass")] string CSharpClass
);

internal sealed record ExtensionSchema(
    string ExtensionId,
    uint SchemaVersion,
    List<ExtensionComponent> Components
);

internal sealed record ExtensionComponent(
    string Kind,
    ExtensionConfiguration Configuration,
    Dictionary<string, int> Flags,
    Dictionary<string, ExtensionCommand> Commands,
    Dictionary<string, ExtensionEvent> Events
);

internal sealed record ExtensionConfiguration(
    string Encoding,
    List<ExtensionConfigurationField> Fields
);

internal sealed record ExtensionConfigurationField(string Name, string Type, List<string>? Values);

internal sealed record ExtensionCommand(ushort Id, string Payload, string Revision);

internal sealed record ExtensionEvent(ushort Id, string Flags, string Payload, string Revision);

internal sealed record Component(
    int Id,
    string Name,
    [property: JsonPropertyName("csharp")] string CSharp,
    string Data,
    bool DataRequired,
    bool Children,
    string ManagedFactory,
    List<string> Capabilities,
    string NativeAdapter
);

internal sealed record EnumSchema(
    string Name,
    [property: JsonPropertyName("csharp")] string CSharp,
    List<EnumVariant> Variants
);

internal sealed record EnumVariant(
    string Name,
    [property: JsonPropertyName("csharp")] string CSharp,
    uint Value
);

internal sealed record LengthMethod(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("param")] string Param,
    [property: JsonPropertyName("px")] string Px,
    [property: JsonPropertyName("percent")] string Percent,
    [property: JsonPropertyName("doc")] string Doc
);

internal sealed record Operation(
    int Id,
    string Name,
    [property: JsonPropertyName("csharp")] string CSharp,
    string Value,
    string Requires,
    string? ManagedApi,
    string? Payload,
    Validation? Validation,
    ManagedMethod? ManagedMethod
);

internal sealed record ManagedMethod(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("param")] string Param,
    [property: JsonPropertyName("default")] double? Default,
    [property: JsonPropertyName("guard")] string? Guard,
    [property: JsonPropertyName("guardMessage")] string? GuardMessage,
    [property: JsonPropertyName("enum")] string? Enum,
    [property: JsonPropertyName("doc")] string Doc
);

internal sealed record Validation(
    string Kind,
    double? Min,
    double? Max,
    bool MinExclusive,
    int Status
);

internal sealed record ResourceSchema(
    int Id,
    string Name,
    [property: JsonPropertyName("csharp")] string CSharp,
    List<ResourceCommandSchema> Commands
);

internal sealed record ResourceCommandSchema(
    int Id,
    string Name,
    [property: JsonPropertyName("csharp")] string CSharp,
    string? Doc
);

internal sealed record ControlEventSchema(
    int Id,
    string Name,
    string Group,
    [property: JsonPropertyName("csharp")] string CSharp,
    string? Doc
);
