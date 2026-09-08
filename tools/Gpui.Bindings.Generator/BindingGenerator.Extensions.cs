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
            if (string.IsNullOrWhiteSpace(component.Configuration))
            {
                throw new InvalidOperationException(
                    $"Extension component '{component.Kind}' needs a configuration description."
                );
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
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        return builder.ToString();
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
        }
        return builder.ToString();
    }
}
