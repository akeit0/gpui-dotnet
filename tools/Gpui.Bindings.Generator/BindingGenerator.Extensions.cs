using System.Text;

internal static partial class BindingGenerator
{
    private static void ValidateExtensionGeneration(string root, ExtensionGeneration generation)
    {
        ValidateRepositoryPath(root, generation.Schema, "extension schema");
        ValidateRepositoryPath(root, generation.CSharpOutput, "extension C# output");
        ValidateRepositoryPath(root, generation.RustOutput, "extension Rust output");
        if (!generation.Schema.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("An extension schema path must end in '.json'.");
        }
        if (!generation.CSharpOutput.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("An extension C# output path must end in '.cs'.");
        }
        if (!generation.RustOutput.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("An extension Rust output path must end in '.rs'.");
        }
        if (!File.Exists(Path.Combine(root, generation.Schema)))
        {
            throw new InvalidOperationException(
                $"Extension schema '{generation.Schema}' does not exist."
            );
        }
        if (string.IsNullOrWhiteSpace(generation.CSharpNamespace))
        {
            throw new InvalidOperationException("An extension C# namespace is required.");
        }
        foreach (var segment in generation.CSharpNamespace.Split('.'))
        {
            ValidateCSharpIdentifier(segment, "extension C# namespace segment");
        }
        ValidateCSharpIdentifier(generation.CSharpClass, "extension C# class");
    }

    private static void ValidateRepositoryPath(string root, string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new InvalidOperationException(
                $"The {description} path must be repository-relative."
            );
        }
        var relative = Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(root, path)));
        if (
            relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relative)
        )
        {
            throw new InvalidOperationException($"The {description} path escapes the repository.");
        }
    }

    private static void Validate(ExtensionSchema schema, string path)
    {
        ValidateExtensionIdentifier(schema.ExtensionId, $"extension ID in {path}");
        if (schema.SchemaVersion == 0)
        {
            throw new InvalidOperationException(
                $"Extension schema '{path}' needs a non-zero version."
            );
        }
        if (schema.Components is not { Count: > 0 })
        {
            throw new InvalidOperationException(
                $"Extension schema '{path}' must declare at least one component."
            );
        }
        EnsureUnique(
            schema.Components.Select(component => component.Kind),
            "extension component kind"
        );
        EnsureUnique(
            schema.Components.Select(component => Pascal(component.Kind)),
            "generated extension component C# name"
        );
        foreach (var component in schema.Components)
        {
            ValidateName(component.Kind, "extension component kind");
            if (
                component.Configuration is null
                || component.Configuration.Encoding != "lines"
                || component.Configuration.Fields is not { Count: > 0 }
            )
            {
                throw new InvalidOperationException(
                    $"Extension component '{component.Kind}' needs a non-empty 'lines' configuration."
                );
            }
            EnsureUnique(
                component.Configuration.Fields.Select(field => field.Name),
                $"configuration field on extension component {component.Kind}"
            );
            EnsureUnique(
                component.Configuration.Fields.Select(field => Pascal(field.Name)),
                $"generated configuration field name on extension component {component.Kind}"
            );
            foreach (var field in component.Configuration.Fields)
            {
                ValidateName(
                    field.Name,
                    $"configuration field on extension component {component.Kind}"
                );
                if (
                    field.Type
                    is not (
                        "flags"
                        or "string"
                        or "u32"
                        or "u64"
                        or "f32"
                        or "bool"
                        or "event"
                        or "enum"
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"Extension configuration field '{field.Name}' has unsupported type '{field.Type}'."
                    );
                }
                if (field.Type == "enum")
                {
                    if (field.Values is not { Count: > 0 })
                    {
                        throw new InvalidOperationException(
                            $"Extension enum configuration field '{field.Name}' needs values."
                        );
                    }
                    EnsureUnique(field.Values, $"value on extension enum field {field.Name}");
                    EnsureUnique(
                        field.Values.Select(Pascal),
                        $"generated value on extension enum field {field.Name}"
                    );
                    foreach (var value in field.Values)
                    {
                        ValidateName(value, $"value on extension enum field {field.Name}");
                    }
                }
                else if (field.Values is { Count: > 0 })
                {
                    throw new InvalidOperationException(
                        $"Only enum configuration fields may declare values ('{field.Name}')."
                    );
                }
                if (field.Type == "flags" && component.Flags is not { Count: > 0 })
                {
                    throw new InvalidOperationException(
                        $"Extension configuration field '{field.Name}' uses flags but the component declares none."
                    );
                }
            }
            if (component.Flags is null)
            {
                throw new InvalidOperationException(
                    $"Extension component '{component.Kind}' needs a flags object."
                );
            }
            if (component.Commands is null)
            {
                throw new InvalidOperationException(
                    $"Extension component '{component.Kind}' needs a commands object."
                );
            }
            if (component.Events is null)
            {
                throw new InvalidOperationException(
                    $"Extension component '{component.Kind}' needs an events object."
                );
            }
            EnsureUnique(
                component.Flags.Values,
                $"flag bit on extension component {component.Kind}"
            );
            EnsureUnique(
                component.Flags.Keys.Select(Pascal),
                $"generated flag C# name on extension component {component.Kind}"
            );
            foreach (var (name, bit) in component.Flags)
            {
                ValidateName(name, $"flag on extension component {component.Kind}");
                if (bit is < 0 or > 31)
                {
                    throw new InvalidOperationException(
                        $"Extension flag '{name}' must use a bit between 0 and 31."
                    );
                }
            }
            EnsureUnique(
                component.Commands.Values.Select(command => command.Id),
                $"command ID on extension component {component.Kind}"
            );
            EnsureUnique(
                component.Commands.Keys.Select(Pascal),
                $"generated command C# name on extension component {component.Kind}"
            );
            foreach (var (name, command) in component.Commands)
            {
                ValidateName(name, $"command on extension component {component.Kind}");
                if (command.Id == 0)
                {
                    throw new InvalidOperationException(
                        $"Extension command '{name}' needs a non-zero ID."
                    );
                }
                if (string.IsNullOrWhiteSpace(command.Payload))
                {
                    throw new InvalidOperationException(
                        $"Extension command '{name}' needs a payload description."
                    );
                }
                if (string.IsNullOrWhiteSpace(command.Revision))
                {
                    throw new InvalidOperationException(
                        $"Extension command '{name}' needs a revision policy."
                    );
                }
            }
            EnsureUnique(
                component.Events.Values.Select(extensionEvent => extensionEvent.Id),
                $"event ID on extension component {component.Kind}"
            );
            EnsureUnique(
                component.Events.Keys.Select(Pascal),
                $"generated event C# name on extension component {component.Kind}"
            );
            foreach (var (name, extensionEvent) in component.Events)
            {
                ValidateName(name, $"event on extension component {component.Kind}");
                if (extensionEvent.Id == 0 || extensionEvent.Id >= 0x8000)
                {
                    throw new InvalidOperationException(
                        $"Extension event '{name}' needs an ID between 1 and 32767."
                    );
                }
                if (string.IsNullOrWhiteSpace(extensionEvent.Flags))
                {
                    throw new InvalidOperationException(
                        $"Extension event '{name}' needs a flags description."
                    );
                }
                if (string.IsNullOrWhiteSpace(extensionEvent.Payload))
                {
                    throw new InvalidOperationException(
                        $"Extension event '{name}' needs a payload description."
                    );
                }
                if (string.IsNullOrWhiteSpace(extensionEvent.Revision))
                {
                    throw new InvalidOperationException(
                        $"Extension event '{name}' needs a revision policy."
                    );
                }
            }
        }
    }

    private static void ValidateExtensionIdentifier(string value, string description)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > 127
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            )
        )
        {
            throw new InvalidOperationException(
                $"The {description} must contain only ASCII letters, digits, '.', '-', and '_'."
            );
        }
    }

    private static string GenerateExtensionCSharp(
        ExtensionSchema schema,
        ulong hash,
        ExtensionGeneration generation
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("// @generated by Gpui.Bindings.Generator. Do not edit.");
        builder.AppendLine();
        builder.AppendLine($"namespace {generation.CSharpNamespace};");
        builder.AppendLine();
        builder.AppendLine($"internal static class {generation.CSharpClass}");
        builder.AppendLine("{");
        builder.AppendLine($"    internal const string ExtensionId = \"{schema.ExtensionId}\";");
        builder.AppendLine($"    internal const uint SchemaVersion = {schema.SchemaVersion}u;");
        builder.AppendLine($"    internal const ulong SchemaHash = 0x{hash:X16}UL;");
        foreach (var component in schema.Components)
        {
            builder.AppendLine();
            builder.AppendLine($"    internal static class {Pascal(component.Kind)}");
            builder.AppendLine("    {");
            builder.AppendLine($"        internal const string Kind = \"{component.Kind}\";");
            foreach (var (name, bit) in component.Flags)
            {
                builder.AppendLine(
                    $"        internal const uint Flag{Pascal(name)} = 1u << {bit};"
                );
            }
            foreach (var (name, command) in component.Commands)
            {
                builder.AppendLine(
                    $"        internal const ushort Command{Pascal(name)} = {command.Id};"
                );
            }
            foreach (var (name, extensionEvent) in component.Events)
            {
                builder.AppendLine(
                    $"        internal const ushort Event{Pascal(name)} = {extensionEvent.Id};"
                );
            }
            if (component.Flags.Count > 0)
            {
                builder.AppendLine("        internal const uint KnownFlags =");
                var flags = component.Flags.Keys.ToArray();
                for (var index = 0; index < flags.Length; index++)
                {
                    var suffix = index == flags.Length - 1 ? ";" : string.Empty;
                    var prefix = index == 0 ? "            " : "            | ";
                    builder.AppendLine($"{prefix}Flag{Pascal(flags[index])}{suffix}");
                }
            }
            foreach (
                var field in component.Configuration.Fields.Where(field => field.Type == "enum")
            )
            {
                builder.AppendLine();
                builder.AppendLine($"        internal enum {Pascal(field.Name)}");
                builder.AppendLine("        {");
                foreach (var value in field.Values!)
                {
                    builder.AppendLine($"            {Pascal(value)},");
                }
                builder.AppendLine("        }");
            }
            AppendCSharpConfigurationEncoder(builder, component);
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendCSharpConfigurationEncoder(
        StringBuilder builder,
        ExtensionComponent component
    )
    {
        var fields = component.Configuration.Fields;
        var parameters = string.Join(
            ", ",
            fields.Select(field => $"{CSharpConfigurationType(field)} {Camel(field.Name)}")
        );
        builder.AppendLine();
        builder.AppendLine($"        internal static string EncodeConfiguration({parameters})");
        builder.AppendLine("        {");
        foreach (var field in fields)
        {
            var name = Camel(field.Name);
            switch (field.Type)
            {
                case "flags":
                    builder.AppendLine($"            if (({name} & ~KnownFlags) != 0)");
                    builder.AppendLine("            {");
                    builder.AppendLine(
                        $"                throw new global::System.ArgumentOutOfRangeException(nameof({name}), \"Unknown extension flags were supplied.\");"
                    );
                    builder.AppendLine("            }");
                    break;
                case "string":
                    builder.AppendLine(
                        $"            global::System.ArgumentNullException.ThrowIfNull({name});"
                    );
                    builder.AppendLine(
                        $"            if ({name}.Contains('\\0') || {name}.Contains('\\n') || {name}.Contains('\\r'))"
                    );
                    builder.AppendLine("            {");
                    builder.AppendLine(
                        $"                throw new global::System.ArgumentException(\"Extension configuration strings cannot contain NUL or newline characters.\", nameof({name}));"
                    );
                    builder.AppendLine("            }");
                    break;
                case "f32":
                    builder.AppendLine($"            if (!float.IsFinite({name}))");
                    builder.AppendLine("            {");
                    builder.AppendLine(
                        $"                throw new global::System.ArgumentOutOfRangeException(nameof({name}), \"Extension configuration numbers must be finite.\");"
                    );
                    builder.AppendLine("            }");
                    break;
                case "enum":
                    builder.AppendLine($"            var {name}Value = {name} switch");
                    builder.AppendLine("            {");
                    foreach (var value in field.Values!)
                    {
                        builder.AppendLine(
                            $"                {Pascal(field.Name)}.{Pascal(value)} => \"{value}\","
                        );
                    }
                    builder.AppendLine(
                        $"                _ => throw new global::System.ArgumentOutOfRangeException(nameof({name})),"
                    );
                    builder.AppendLine("            };");
                    break;
            }
        }
        var values = fields.Select(CSharpConfigurationValue);
        builder.AppendLine(
            $"            return global::System.String.Create(global::System.Globalization.CultureInfo.InvariantCulture, $\"{string.Join("\\n", values.Select(value => $"{{{value}}}"))}\");"
        );
        builder.AppendLine("        }");
    }

    private static string CSharpConfigurationType(ExtensionConfigurationField field) =>
        field.Type switch
        {
            "flags" or "u32" => "uint",
            "u64" or "event" => "ulong",
            "f32" => "float",
            "bool" => "bool",
            "string" => "string",
            "enum" => Pascal(field.Name),
            _ => throw new InvalidOperationException(
                $"Unknown extension field type '{field.Type}'."
            ),
        };

    private static string CSharpConfigurationValue(ExtensionConfigurationField field)
    {
        var name = Camel(field.Name);
        return field.Type switch
        {
            "bool" => $"({name} ? 1 : 0)",
            "enum" => $"{name}Value",
            _ => name,
        };
    }

    private static string GenerateExtensionRust(ExtensionSchema schema, ulong hash)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// @generated by Gpui.Bindings.Generator. Do not edit.");
        builder.AppendLine($"pub const EXTENSION_ID: &str = \"{schema.ExtensionId}\";");
        builder.AppendLine($"pub const SCHEMA_VERSION: u32 = {schema.SchemaVersion};");
        builder.AppendLine($"pub const SCHEMA_HASH: u64 = 0x{hash:X16};");
        foreach (var component in schema.Components)
        {
            var componentName = UpperSnake(component.Kind);
            builder.AppendLine();
            builder.AppendLine(
                $"pub const COMPONENT_{componentName}: &str = \"{component.Kind}\";"
            );
            foreach (var (name, bit) in component.Flags)
            {
                builder.AppendLine(
                    $"pub const {componentName}_FLAG_{UpperSnake(name)}: u32 = 1 << {bit};"
                );
            }
            foreach (var (name, command) in component.Commands)
            {
                builder.AppendLine(
                    $"pub const {componentName}_COMMAND_{UpperSnake(name)}: u16 = {command.Id};"
                );
            }
            foreach (var (name, extensionEvent) in component.Events)
            {
                builder.AppendLine(
                    $"pub const {componentName}_EVENT_{UpperSnake(name)}: u16 = {extensionEvent.Id};"
                );
            }
            if (component.Flags.Count > 0)
            {
                var flags = component.Flags.Keys.ToArray();
                var firstSuffix = flags.Length == 1 ? ";" : string.Empty;
                builder.AppendLine(
                    $"pub const {componentName}_KNOWN_FLAGS: u32 = {componentName}_FLAG_{UpperSnake(flags[0])}{firstSuffix}"
                );
                for (var index = 1; index < flags.Length; index++)
                {
                    var suffix = index == flags.Length - 1 ? ";" : string.Empty;
                    builder.AppendLine(
                        $"    | {componentName}_FLAG_{UpperSnake(flags[index])}{suffix}"
                    );
                }
            }
            AppendRustConfigurationParser(builder, component);
        }
        return builder.ToString();
    }

    private static void AppendRustConfigurationParser(
        StringBuilder builder,
        ExtensionComponent component
    )
    {
        var fields = component.Configuration.Fields;
        var componentName = Pascal(component.Kind);
        var borrows = fields.Any(field => field.Type == "string");
        foreach (var field in fields.Where(field => field.Type == "enum"))
        {
            builder.AppendLine();
            builder.AppendLine("#[derive(Clone, Copy, Debug, Eq, PartialEq)]");
            builder.AppendLine($"pub enum {componentName}{Pascal(field.Name)} {{");
            foreach (var value in field.Values!)
            {
                builder.AppendLine($"    {Pascal(value)},");
            }
            builder.AppendLine("}");
        }
        var lifetime = borrows ? "<'a>" : string.Empty;
        builder.AppendLine();
        builder.AppendLine("#[derive(Clone, Debug, PartialEq)]");
        builder.AppendLine($"pub struct {componentName}Configuration{lifetime} {{");
        foreach (var field in fields)
        {
            builder.AppendLine(
                $"    pub {field.Name}: {RustConfigurationType(componentName, field, borrows)},"
            );
        }
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine($"impl{lifetime} {componentName}Configuration{lifetime} {{");
        var parseLifetime = borrows ? "value: &'a str" : "value: &str";
        builder.AppendLine($"    pub fn parse({parseLifetime}) -> Option<Self> {{");
        builder.AppendLine("        let mut fields = value.split('\\n');");
        foreach (var field in fields)
        {
            var source = $"fields.next()?";
            var parsed = field.Type switch
            {
                "string" => source,
                "bool" => $"match {source} {{ \"0\" => false, \"1\" => true, _ => return None }}",
                "enum" => RustEnumParser(componentName, field, source),
                _ =>
                    $"{source}.parse::<{RustConfigurationType(componentName, field, borrows)}>().ok()?",
            };
            builder.AppendLine($"        let {field.Name} = {parsed};");
        }
        builder.AppendLine("        if fields.next().is_some() {");
        builder.AppendLine("            return None;");
        builder.AppendLine("        }");
        foreach (var field in fields)
        {
            if (field.Type == "f32")
            {
                builder.AppendLine($"        if !{field.Name}.is_finite() {{");
                builder.AppendLine("            return None;");
                builder.AppendLine("        }");
            }
            if (field.Type == "flags")
            {
                builder.AppendLine(
                    $"        if {field.Name} & !{UpperSnake(component.Kind)}_KNOWN_FLAGS != 0 {{"
                );
                builder.AppendLine("            return None;");
                builder.AppendLine("        }");
            }
        }
        builder.AppendLine("        Some(Self {");
        foreach (var field in fields)
        {
            builder.AppendLine($"            {field.Name},");
        }
        builder.AppendLine("        })");
        builder.AppendLine("    }");
        builder.AppendLine("}");
    }

    private static string RustConfigurationType(
        string componentName,
        ExtensionConfigurationField field,
        bool borrows
    ) =>
        field.Type switch
        {
            "flags" or "u32" => "u32",
            "u64" or "event" => "u64",
            "f32" => "f32",
            "bool" => "bool",
            "string" => borrows ? "&'a str" : "&str",
            "enum" => componentName + Pascal(field.Name),
            _ => throw new InvalidOperationException(
                $"Unknown extension field type '{field.Type}'."
            ),
        };

    private static string RustEnumParser(
        string componentName,
        ExtensionConfigurationField field,
        string source
    )
    {
        var cases = string.Join(
            ", ",
            field.Values!.Select(value =>
                $"\"{value}\" => {componentName}{Pascal(field.Name)}::{Pascal(value)}"
            )
        );
        return $"match {source} {{ {cases}, _ => return None }}";
    }
}
