using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

return BindingGenerator.Run(args);

internal static class BindingGenerator
{
    private const string SchemaPath = "bindings/schema.json";
    private const string ExtensionManifestPath = "bindings/extensions.json";
    private const string CSharpOutputPath = "src/Gpui/Rendering/Semantic.g.cs";
    private const string RustOutputPath = "crates/gpui-dotnet/src/semantic.g.rs";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    public static int Run(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault() ?? "generate";
            if (command is not ("generate" or "verify"))
            {
                throw new InvalidOperationException(
                    "Usage: Gpui.Bindings.Generator [generate|verify] [--root <repository>]"
                );
            }

            var root = GetRoot(args);
            var source = File.ReadAllText(Path.Combine(root, SchemaPath), Encoding.UTF8);
            var schema =
                JsonSerializer.Deserialize<BindingSchema>(source, JsonOptions)
                ?? throw new InvalidOperationException($"{SchemaPath} is empty.");

            Validate(schema);
            var canonical = JsonSerializer.SerializeToUtf8Bytes(schema, JsonOptions);
            var digest = SHA256.HashData(canonical);
            var hash = BinaryPrimitives.ReadUInt64LittleEndian(digest);

            var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CSharpOutputPath] = GenerateCSharp(schema, hash),
                [RustOutputPath] = GenerateRust(schema, hash),
            };

            var extensionManifestSource = File.ReadAllText(
                Path.Combine(root, ExtensionManifestPath),
                Encoding.UTF8
            );
            var extensionManifest =
                JsonSerializer.Deserialize<ExtensionManifest>(extensionManifestSource, JsonOptions)
                ?? throw new InvalidOperationException($"{ExtensionManifestPath} is empty.");
            if (extensionManifest.Schemas is not { Count: > 0 })
            {
                throw new InvalidOperationException(
                    $"{ExtensionManifestPath} must register at least one extension schema."
                );
            }
            var extensionIds = new HashSet<string>(StringComparer.Ordinal);
            var extensionSchemaPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var generation in extensionManifest.Schemas)
            {
                ValidateExtensionGeneration(root, generation);
                if (!extensionSchemaPaths.Add(generation.Schema))
                {
                    throw new InvalidOperationException(
                        $"Extension schema '{generation.Schema}' is registered more than once."
                    );
                }
                var extensionSource = File.ReadAllText(
                    Path.Combine(root, generation.Schema),
                    Encoding.UTF8
                );
                var extensionSchema =
                    JsonSerializer.Deserialize<ExtensionSchema>(extensionSource, JsonOptions)
                    ?? throw new InvalidOperationException($"{generation.Schema} is empty.");
                Validate(extensionSchema, generation.Schema);
                if (!extensionIds.Add(extensionSchema.ExtensionId))
                {
                    throw new InvalidOperationException(
                        $"Extension ID '{extensionSchema.ExtensionId}' is registered more than once."
                    );
                }
                var extensionCanonical = JsonSerializer.SerializeToUtf8Bytes(
                    extensionSchema,
                    JsonOptions
                );
                var extensionDigest = SHA256.HashData(extensionCanonical);
                var extensionHash = BinaryPrimitives.ReadUInt64LittleEndian(extensionDigest);
                if (extensionHash == 0)
                {
                    throw new InvalidOperationException(
                        $"Extension schema '{generation.Schema}' produced the reserved zero hash."
                    );
                }
                if (
                    !outputs.TryAdd(
                        generation.CSharpOutput,
                        GenerateExtensionCSharp(extensionSchema, extensionHash, generation)
                    )
                    || !outputs.TryAdd(
                        generation.RustOutput,
                        GenerateExtensionRust(extensionSchema, extensionHash)
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"Extension generation for '{generation.Schema}' has a duplicate output path."
                    );
                }
            }

            var stale = outputs
                .Where(output =>
                    !File.Exists(Path.Combine(root, output.Key))
                    || Normalize(File.ReadAllText(Path.Combine(root, output.Key), Encoding.UTF8))
                        != Normalize(output.Value)
                )
                .Select(output => output.Key)
                .ToArray();

            if (command == "verify")
            {
                if (stale.Length == 0)
                {
                    Console.WriteLine("Semantic and extension bindings are current.");
                    return 0;
                }

                Console.Error.WriteLine(
                    $"Generated semantic or extension bindings are stale: {string.Join(", ", stale)}"
                );
                Console.Error.WriteLine(
                    "Run: dotnet run --project tools/Gpui.Bindings.Generator -- generate"
                );
                return 1;
            }

            foreach (var path in stale)
            {
                var absolutePath = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                File.WriteAllText(absolutePath, outputs[path], new UTF8Encoding(false));
                Console.WriteLine($"Generated {path}");
            }

            if (stale.Length == 0)
            {
                Console.WriteLine("Semantic and extension bindings are already current.");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string GetRoot(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != "--root")
            {
                continue;
            }
            if (++i == args.Length)
            {
                throw new InvalidOperationException("--root requires a repository path.");
            }
            return Path.GetFullPath(args[i]);
        }

        for (
            var directory = new DirectoryInfo(Environment.CurrentDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, SchemaPath)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root. Pass --root <repository>."
        );
    }

    private static void Validate(BindingSchema schema)
    {
        if (schema.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("schemaVersion must be positive.");
        }
        if (schema.Capabilities.Count == 0)
        {
            throw new InvalidOperationException("At least one capability is required.");
        }
        if (schema.Capabilities.Count > 64)
        {
            throw new InvalidOperationException(
                "The wire metadata supports at most 64 capabilities."
            );
        }

        EnsureUnique(schema.Capabilities, "capability");
        foreach (var capability in schema.Capabilities)
        {
            ValidateName(capability, "capability");
        }

        EnsureUnique(schema.Components.Select(component => component.Id), "component ID");
        EnsureUnique(schema.Components.Select(component => component.Name), "component name");
        EnsureUnique(schema.Components.Select(component => component.CSharp), "component C# name");
        EnsureUnique(schema.Operations.Select(operation => operation.Id), "operation ID");
        EnsureUnique(schema.Operations.Select(operation => operation.Name), "operation name");

        if (schema.Components.Count == 0 || schema.Operations.Count == 0)
        {
            throw new InvalidOperationException(
                "The schema must define components and operations."
            );
        }

        var capabilityNames = schema.Capabilities.ToHashSet(StringComparer.Ordinal);
        foreach (var component in schema.Components)
        {
            ValidateId(component.Id, $"component {component.Name}");
            ValidateName(component.Name, "component");
            ValidateCSharpIdentifier(component.CSharp, "component");
            ValidateName(component.NativeAdapter, "native adapter");

            if (component.Data is not ("none" or "utf8"))
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} has unknown data kind '{component.Data}'."
                );
            }
            if (component.DataRequired && component.Data == "none")
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} cannot require data when data is 'none'."
                );
            }
            if (
                component.ManagedFactory is not ("manual" or "container" or "idContainer" or "leaf")
            )
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} has unknown managedFactory '{component.ManagedFactory}'."
                );
            }

            var validFactoryShape = component.ManagedFactory switch
            {
                "manual" => true,
                "container" => component.Data == "none" && component.Children,
                "idContainer" => component.Data == "utf8"
                    && component.DataRequired
                    && component.Children,
                "leaf" => component.Data == "none" && !component.Children,
                _ => false,
            };
            if (!validFactoryShape)
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} managedFactory is incompatible with its data/children shape."
                );
            }

            EnsureUnique(component.Capabilities, $"capability on component {component.Name}");
            foreach (var capability in component.Capabilities)
            {
                if (!capabilityNames.Contains(capability))
                {
                    throw new InvalidOperationException(
                        $"Component {component.Name} references unknown capability '{capability}'."
                    );
                }
            }
            if (
                component.Children
                && !component.Capabilities.Contains("parent", StringComparer.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} allows children but does not have the parent capability."
                );
            }
        }

        foreach (var operation in schema.Operations)
        {
            ValidateId(operation.Id, $"operation {operation.Name}");
            ValidateName(operation.Name, "operation");
            ValidateCSharpIdentifier(operation.CSharp, "operation");
            if (operation.Value is not ("none" or "f32" or "f32x2" or "u32" or "callback" or "u64"))
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has unknown value kind '{operation.Value}'."
                );
            }
            if (!capabilityNames.Contains(operation.Requires))
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} requires unknown capability '{operation.Requires}'."
                );
            }
            if (
                !schema.Components.Any(component =>
                    component.Capabilities.Contains(operation.Requires)
                )
            )
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} requires capability '{operation.Requires}' that no component provides."
                );
            }
            if (
                operation.ManagedApi
                is not (null or "extension" or "pixels" or "color" or "bool" or "click")
            )
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has unknown managedApi '{operation.ManagedApi}'."
                );
            }

            var expectedValue = operation.ManagedApi switch
            {
                "extension" => "none",
                "pixels" => "f32",
                "color" => "u32",
                "bool" => "u32",
                "click" => "callback",
                _ => operation.Value,
            };
            if (expectedValue != operation.Value)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} managedApi is incompatible with value kind {operation.Value}."
                );
            }

            if (operation.Payload is not (null or "none" or "event"))
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has unknown payload kind '{operation.Payload}'."
                );
            }
            if (operation.Payload == "event" && operation.Value != "callback")
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} event payloads require callback values."
                );
            }

            ValidateValidation(operation);
        }
    }

    private static void ValidateValidation(Operation operation)
    {
        var validation = operation.Validation;
        if (validation is null)
        {
            return;
        }
        if (validation.Status >= 0)
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} validation status must be negative."
            );
        }

        if (
            validation.Kind
            is not (
                "bool"
                or "u32Min"
                or "u32Max"
                or "u32Range"
                or "f32Min"
                or "f32Range"
                or "packedTableColumn"
            )
        )
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} has unknown validation kind '{validation.Kind}'."
            );
        }

        if (
            validation.Kind is "bool" or "u32Min" or "u32Max" or "u32Range"
            && operation.Value != "u32"
        )
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} uses a {validation.Kind} validation but is not u32."
            );
        }
        if (validation.Kind is "f32Min" or "f32Range" && operation.Value != "f32")
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} uses a {validation.Kind} validation but is not f32."
            );
        }
        if (validation.Kind == "packedTableColumn" && operation.Value != "u64")
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} uses packedTableColumn validation but is not u64."
            );
        }
        if (validation.MinExclusive && validation.Kind != "f32Min")
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} can use minExclusive only with f32Min validation."
            );
        }

        if (validation.Kind is "u32Min" or "u32Max" or "u32Range")
        {
            if (validation.Kind is "u32Min" or "u32Range")
            {
                ValidateIntegerBound(validation.Min, operation, "min");
            }
            if (validation.Kind is "u32Max" or "u32Range")
            {
                ValidateIntegerBound(validation.Max, operation, "max");
            }
            if (validation.Kind == "u32Range" && validation.Min!.Value > validation.Max!.Value)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has a validation range with min greater than max."
                );
            }
        }
        else if (validation.Kind is "f32Min" or "f32Range")
        {
            if (validation.Min is not double min || !double.IsFinite(min))
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} requires a finite validation min."
                );
            }
            if (
                validation.Kind == "f32Range"
                && (validation.Max is not double max || !double.IsFinite(max))
            )
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} requires a finite validation max."
                );
            }
            if (validation.Kind == "f32Range" && validation.Min!.Value > validation.Max!.Value)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has a validation range with min greater than max."
                );
            }
        }
    }

    private static void ValidateIntegerBound(double? value, Operation operation, string name)
    {
        if (
            value is not double bound
            || !double.IsFinite(bound)
            || bound < 0
            || bound != Math.Truncate(bound)
            || bound > uint.MaxValue
        )
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} requires a non-negative integer validation {name}."
            );
        }
    }

    private static void ValidateExtensionGeneration(
        string root,
        ExtensionGeneration generation
    )
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
            throw new InvalidOperationException($"The {description} path must be repository-relative.");
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
            throw new InvalidOperationException($"Extension schema '{path}' needs a non-zero version.");
        }
        if (schema.Components is not { Count: > 0 })
        {
            throw new InvalidOperationException(
                $"Extension schema '{path}' must declare at least one component."
            );
        }
        EnsureUnique(schema.Components.Select(component => component.Kind), "extension component kind");
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
            EnsureUnique(component.Flags.Values, $"flag bit on extension component {component.Kind}");
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
        }
    }

    private static void ValidateExtensionIdentifier(string value, string description)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > 127
            || value.Any(character =>
                !(
                    char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '-' or '_'
                )
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

    private static string GenerateCSharp(BindingSchema schema, ulong hash)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("using System.Runtime.CompilerServices;");
        builder.AppendLine("using Gpui.Interop;");
        builder.AppendLine();
        builder.AppendLine("namespace Gpui.Interop");
        builder.AppendLine("{");
        builder.AppendLine("    internal enum ComponentId : ushort");
        builder.AppendLine("    {");
        foreach (var component in schema.Components)
        {
            builder.AppendLine($"        {component.CSharp} = {component.Id},");
        }
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    internal enum OpCode : ushort");
        builder.AppendLine("    {");
        foreach (var operation in schema.Operations)
        {
            builder.AppendLine($"        {Pascal(operation.Name)} = {operation.Id},");
        }
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    internal enum ValueKind : ushort");
        builder.AppendLine("    {");
        builder.AppendLine("        None = 0,");
        builder.AppendLine("        F32 = 1,");
        builder.AppendLine("        U32 = 2,");
        builder.AppendLine("        Callback = 3,");
        builder.AppendLine("        U64 = 4,");
        builder.AppendLine("        F32x2 = 5,");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    internal static class SemanticRegistry");
        builder.AppendLine("    {");
        builder.AppendLine($"        internal const uint SchemaVersion = {schema.SchemaVersion};");
        builder.AppendLine($"        internal const ulong SchemaHash = 0x{hash:X16}UL;");
        builder.AppendLine();
        builder.AppendLine(
            "        internal static bool IsKnownComponent(ComponentId component) => component switch"
        );
        builder.AppendLine("        {");
        foreach (var component in schema.Components)
        {
            builder.AppendLine($"            ComponentId.{component.CSharp} => true,");
        }
        builder.AppendLine("            _ => false,");
        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine(
            "        internal static bool IsDataRequired(ComponentId component) => component switch"
        );
        builder.AppendLine("        {");
        foreach (var component in schema.Components.Where(c => c.DataRequired))
        {
            builder.AppendLine($"            ComponentId.{component.CSharp} => true,");
        }
        builder.AppendLine("            _ => false,");
        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine(
            "        internal static ValueKind? ExpectedValueKind(OpCode operation) => operation switch"
        );
        builder.AppendLine("        {");
        foreach (var operation in schema.Operations)
        {
            builder.AppendLine(
                $"            OpCode.{Pascal(operation.Name)} => ValueKind.{Pascal(operation.Value)},"
            );
        }
        builder.AppendLine("            _ => null,");
        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine(
            "        private static ulong Capabilities(ComponentId component) => component switch"
        );
        builder.AppendLine("        {");
        foreach (var component in schema.Components)
        {
            builder.AppendLine(
                $"            ComponentId.{component.CSharp} => 0x{CapabilityMask(schema, component.Capabilities):X16}UL,"
            );
        }
        builder.AppendLine("            _ => 0,");
        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine(
            "        private static ulong RequiredCapability(OpCode operation) => operation switch"
        );
        builder.AppendLine("        {");
        foreach (var operation in schema.Operations)
        {
            builder.AppendLine(
                $"            OpCode.{Pascal(operation.Name)} => 0x{CapabilityBit(schema, operation.Requires):X16}UL,"
            );
        }
        builder.AppendLine("            _ => 0,");
        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine(
            "        internal static bool IsAllowed(ComponentId component, OpCode operation)"
        );
        builder.AppendLine("        {");
        builder.AppendLine("            var required = RequiredCapability(operation);");
        builder.AppendLine(
            "            return required != 0 && (Capabilities(component) & required) == required;"
        );
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine(
            "        internal static bool AllowsPayload(OpCode operation) => operation switch"
        );
        builder.AppendLine("        {");
        foreach (
            var operation in schema.Operations.Where(operation => operation.Payload == "event")
        )
        {
            builder.AppendLine($"            OpCode.{Pascal(operation.Name)} => true,");
        }
        builder.AppendLine("            _ => false,");
        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine(
            "        internal static int PayloadError(OpCode operation, ulong a) => operation switch"
        );
        builder.AppendLine("        {");
        foreach (
            var operation in schema.Operations.Where(operation => operation.Validation is not null)
        )
        {
            builder.AppendLine(
                $"            OpCode.{Pascal(operation.Name)} when !({CSharpValidationExpression(operation.Validation!)}) => {operation.Validation!.Status},"
            );
        }
        builder.AppendLine("            _ => 0,");
        builder.AppendLine("        };");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("namespace Gpui");
        builder.AppendLine("{");
        foreach (var capability in schema.Capabilities)
        {
            builder.AppendLine($"    public interface {CapabilityInterface(capability)} {{ }}");
        }
        builder.AppendLine();
        foreach (var component in schema.Components)
        {
            var interfaces = string.Join(", ", component.Capabilities.Select(CapabilityInterface));
            builder.AppendLine(
                $"    public readonly struct {component.CSharp}Tag : {interfaces} {{ }}"
            );
        }
        builder.AppendLine();
        builder.AppendLine("    public readonly unsafe ref partial struct RenderContext");
        builder.AppendLine("    {");
        foreach (
            var component in schema.Components.Where(component =>
                component.ManagedFactory != "manual"
            )
        )
        {
            AppendManagedFactory(builder, component);
        }
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static partial class ElementExtensions");
        builder.AppendLine("    {");
        AppendParentApis(builder);
        foreach (
            var operation in schema.Operations.Where(operation =>
                operation.ManagedApi is not null and not "click"
            )
        )
        {
            AppendManagedApi(builder, operation);
        }
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendManagedFactory(StringBuilder builder, Component component)
    {
        var elementType = $"Element<{component.CSharp}Tag>";
        var componentId = $"ComponentId.{component.CSharp}";
        builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        switch (component.ManagedFactory)
        {
            case "container":
                builder.AppendLine(
                    $"        public {elementType} {component.CSharp}(params ReadOnlySpan<Element> children)"
                );
                builder.AppendLine("        {");
                builder.AppendLine(
                    $"            var element = ArenaWriter.AddNode<{component.CSharp}Tag>(_arena, {componentId});"
                );
                builder.AppendLine("            ArenaWriter.AddChildren(element.Inner, children);");
                builder.AppendLine("            return element;");
                builder.AppendLine("        }");
                break;
            case "idContainer":
                {
                    // dataRequired components reject empty ids before the FFI boundary; the
                    // native validator would otherwise fail the whole snapshot at decode time.
                    var guard = component.DataRequired;
                    builder.AppendLine(
                        $"        public {elementType} {component.CSharp}(ReadOnlySpan<char> id, params ReadOnlySpan<Element> children)"
                    );
                    builder.AppendLine("        {");
                    if (guard)
                    {
                        builder.AppendLine("            if (id.IsEmpty)");
                        builder.AppendLine("            {");
                        builder.AppendLine(
                            "                throw new ArgumentException(\"A non-empty element id is required.\", nameof(id));"
                        );
                        builder.AppendLine("            }");
                    }
                    builder.AppendLine(
                        $"            var element = ArenaWriter.AddNode<{component.CSharp}Tag>(_arena, {componentId}, id);"
                    );
                    AppendInteractiveOwner(builder, component);
                    builder.AppendLine("            ArenaWriter.AddChildren(element.Inner, children);");
                    builder.AppendLine("            return element;");
                    builder.AppendLine("        }");
                    builder.AppendLine();
                    builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    builder.AppendLine(
                        $"        public {elementType} {component.CSharp}(ReadOnlySpan<byte> utf8Id, params ReadOnlySpan<Element> children)"
                    );
                    builder.AppendLine("        {");
                    if (guard)
                    {
                        builder.AppendLine("            if (utf8Id.IsEmpty)");
                        builder.AppendLine("            {");
                        builder.AppendLine(
                            "                throw new ArgumentException(\"A non-empty element id is required.\", nameof(utf8Id));"
                        );
                        builder.AppendLine("            }");
                    }
                    builder.AppendLine(
                        $"            var element = ArenaWriter.AddNode<{component.CSharp}Tag>(_arena, {componentId}, utf8Id);"
                    );
                    AppendInteractiveOwner(builder, component);
                    builder.AppendLine("            ArenaWriter.AddChildren(element.Inner, children);");
                    builder.AppendLine("            return element;");
                    builder.AppendLine("        }");
                    builder.AppendLine();
                    builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    builder.AppendLine(
                        $"        public {elementType} {component.CSharp}(ReadOnlySpan<char> id, ReadOnlySpan<char> label)"
                    );
                    builder.AppendLine("        {");
                    builder.AppendLine("            var child = Text(label);");
                    builder.AppendLine($"            return {component.CSharp}(id, child);");
                    builder.AppendLine("        }");
                    builder.AppendLine();
                    builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    builder.AppendLine(
                        $"        public {elementType} {component.CSharp}(ReadOnlySpan<char> id, ReadOnlySpan<byte> utf8Label)"
                    );
                    builder.AppendLine("        {");
                    builder.AppendLine("            var child = Text(utf8Label);");
                    builder.AppendLine($"            return {component.CSharp}(id, child);");
                    builder.AppendLine("        }");
                    builder.AppendLine();
                    builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    builder.AppendLine(
                        $"        public {elementType} {component.CSharp}(ReadOnlySpan<byte> utf8Id, ReadOnlySpan<char> label)"
                    );
                    builder.AppendLine("        {");
                    builder.AppendLine("            var child = Text(label);");
                    builder.AppendLine($"            return {component.CSharp}(utf8Id, child);");
                    builder.AppendLine("        }");
                    builder.AppendLine();
                    builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    builder.AppendLine(
                        $"        public {elementType} {component.CSharp}(ReadOnlySpan<byte> utf8Id, ReadOnlySpan<byte> utf8Label)"
                    );
                    builder.AppendLine("        {");
                    builder.AppendLine("            var child = Text(utf8Label);");
                    builder.AppendLine($"            return {component.CSharp}(utf8Id, child);");
                    builder.AppendLine("        }");
                    break;
                }
            case "leaf":
                builder.AppendLine($"        public {elementType} {component.CSharp}() =>");
                builder.AppendLine(
                    $"            ArenaWriter.AddNode<{component.CSharp}Tag>(_arena, {componentId});"
                );
                break;
            default:
                throw new InvalidOperationException();
        }
        builder.AppendLine();
    }

    private static void AppendInteractiveOwner(StringBuilder builder, Component component)
    {
        if (component.Capabilities.Contains("interactive", StringComparer.Ordinal))
        {
            builder.AppendLine("            AddInteractiveOwner(element.Inner);");
        }
    }

    private static void AppendParentApis(StringBuilder builder)
    {
        builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine(
            "        public static Element<TTag> Child<TTag>(this Element<TTag> parent, Element child)"
        );
        builder.AppendLine("            where TTag : unmanaged, IParentElementTag");
        builder.AppendLine("        {");
        builder.AppendLine("            Span<Element> one = [child];");
        builder.AppendLine("            ArenaWriter.AddChildren(parent.Inner, one);");
        builder.AppendLine("            return parent;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine(
            "        public static Element<TTag> Children<TTag>(this Element<TTag> parent, params ReadOnlySpan<Element> children)"
        );
        builder.AppendLine("            where TTag : unmanaged, IParentElementTag");
        builder.AppendLine("        {");
        builder.AppendLine("            ArenaWriter.AddChildren(parent.Inner, children);");
        builder.AppendLine("            return parent;");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void AppendManagedApi(StringBuilder builder, Operation operation)
    {
        var parameter = operation.ManagedApi switch
        {
            "pixels" => ", Pixels value",
            "color" => ", Color color",
            "bool" => ", bool value",
            "click" => ", Callback<ClickEvent> callback",
            _ => string.Empty,
        };
        var statement = operation.ManagedApi switch
        {
            "extension" => $"ArenaWriter.AddNoArg(element.Inner, OpCode.{Pascal(operation.Name)});",
            "pixels" =>
                $"ArenaWriter.AddF32(element.Inner, OpCode.{Pascal(operation.Name)}, value.Value);",
            "color" =>
                $"ArenaWriter.AddU32(element.Inner, OpCode.{Pascal(operation.Name)}, color.Rgba);",
            "bool" =>
                $"ArenaWriter.AddU32(element.Inner, OpCode.{Pascal(operation.Name)}, value ? 1u : 0u);",
            "click" =>
                $"ArenaWriter.AddCallback(element.Inner, OpCode.{Pascal(operation.Name)}, callback.Token);",
            _ => throw new InvalidOperationException(),
        };

        builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine(
            $"        public static Element<TTag> {operation.CSharp}<TTag>(this Element<TTag> element{parameter})"
        );
        builder.AppendLine(
            $"            where TTag : unmanaged, {CapabilityInterface(operation.Requires)}"
        );
        builder.AppendLine("        {");
        if (operation.ManagedApi == "click")
        {
            builder.AppendLine("            if (callback.IsDefault)");
            builder.AppendLine("            {");
            builder.AppendLine(
                "                throw new ArgumentException(\"Event token 0 is reserved.\", nameof(callback));"
            );
            builder.AppendLine("            }");
            builder.AppendLine();
        }
        builder.AppendLine($"            {statement}");
        builder.AppendLine("            return element;");
        builder.AppendLine("        }");
        builder.AppendLine();

        if (operation.ManagedApi == "click")
        {
            builder.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            builder.AppendLine(
                $"        public static Element<TTag> {operation.CSharp}<TTag>(this Element<TTag> element, Callback<ClickEvent> callback, ulong payload)"
            );
            builder.AppendLine(
                $"            where TTag : unmanaged, {CapabilityInterface(operation.Requires)}"
            );
            builder.AppendLine("        {");
            builder.AppendLine("            if (callback.IsDefault)");
            builder.AppendLine("            {");
            builder.AppendLine(
                "                throw new ArgumentException(\"Event token 0 is reserved.\", nameof(callback));"
            );
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine(
                $"            ArenaWriter.AddCallback(element.Inner, OpCode.{Pascal(operation.Name)}, callback.Token, payload);"
            );
            builder.AppendLine("            return element;");
            builder.AppendLine("        }");
            builder.AppendLine();
        }
    }

    private static string GenerateRust(BindingSchema schema, ulong hash)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// @generated by Gpui.Bindings.Generator. Do not edit.");
        builder.AppendLine("#![allow(dead_code)]");
        builder.AppendLine($"pub const SCHEMA_VERSION: u32 = {schema.SchemaVersion};");
        builder.AppendLine($"pub const SCHEMA_HASH: u64 = 0x{hash:X16};");
        builder.AppendLine();
        foreach (var capability in schema.Capabilities)
        {
            builder.AppendLine(
                $"pub const CAPABILITY_{UpperSnake(capability)}: u64 = 0x{CapabilityBit(schema, capability):X16};"
            );
        }
        builder.AppendLine();
        foreach (var component in schema.Components)
        {
            builder.AppendLine(
                $"pub const COMPONENT_{UpperSnake(component.Name)}: u16 = {component.Id};"
            );
        }
        builder.AppendLine();
        foreach (var operation in schema.Operations)
        {
            builder.AppendLine($"pub const OP_{UpperSnake(operation.Name)}: u16 = {operation.Id};");
        }
        builder.AppendLine();
        builder.AppendLine("#[repr(u16)]");
        builder.AppendLine("#[derive(Clone, Copy, Debug, Eq, PartialEq)]");
        builder.AppendLine("pub enum ValueKind {");
        builder.AppendLine("    None = 0,");
        builder.AppendLine("    F32 = 1,");
        builder.AppendLine("    U32 = 2,");
        builder.AppendLine("    Callback = 3,");
        builder.AppendLine("    U64 = 4,");
        builder.AppendLine("    F32x2 = 5,");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("#[derive(Clone, Copy, Debug, Eq, PartialEq)]");
        builder.AppendLine("pub enum DataKind {");
        builder.AppendLine("    None,");
        builder.AppendLine("    Utf8,");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("#[derive(Clone, Copy, Debug, Eq, PartialEq)]");
        builder.AppendLine("pub enum NativeAdapter {");
        foreach (
            var adapter in schema
                .Components.Select(component => component.NativeAdapter)
                .Distinct(StringComparer.Ordinal)
        )
        {
            builder.AppendLine($"    {Pascal(adapter)},");
        }
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("#[derive(Clone, Copy, Debug)]");
        builder.AppendLine("pub struct ComponentMetadata {");
        builder.AppendLine("    pub id: u16,");
        builder.AppendLine("    pub name: &'static str,");
        builder.AppendLine("    pub data_kind: DataKind,");
        builder.AppendLine("    pub data_required: bool,");
        builder.AppendLine("    pub allows_children: bool,");
        builder.AppendLine("    pub capabilities: u64,");
        builder.AppendLine("    pub adapter: NativeAdapter,");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("#[derive(Clone, Copy, Debug)]");
        builder.AppendLine("pub struct OperationMetadata {");
        builder.AppendLine("    pub id: u16,");
        builder.AppendLine("    pub name: &'static str,");
        builder.AppendLine("    pub value_kind: ValueKind,");
        builder.AppendLine("    pub required_capability: u64,");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("impl OperationMetadata {");
        builder.AppendLine("    pub fn applies_to(self, component: u16) -> bool {");
        builder.AppendLine("        component_metadata(component).is_some_and(|metadata| {");
        builder.AppendLine(
            "            metadata.capabilities & self.required_capability == self.required_capability"
        );
        builder.AppendLine("        })");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("pub const COMPONENTS: &[ComponentMetadata] = &[");
        foreach (var component in schema.Components)
        {
            builder.AppendLine("    ComponentMetadata {");
            builder.AppendLine($"        id: COMPONENT_{UpperSnake(component.Name)},");
            builder.AppendLine($"        name: \"{component.Name}\",");
            builder.AppendLine($"        data_kind: DataKind::{Pascal(component.Data)},");
            builder.AppendLine(
                $"        data_required: {component.DataRequired.ToString().ToLowerInvariant()},"
            );
            builder.AppendLine(
                $"        allows_children: {component.Children.ToString().ToLowerInvariant()},"
            );
            builder.AppendLine(
                $"        capabilities: 0x{CapabilityMask(schema, component.Capabilities):X16},"
            );
            builder.AppendLine(
                $"        adapter: NativeAdapter::{Pascal(component.NativeAdapter)},"
            );
            builder.AppendLine("    },");
        }
        builder.AppendLine("];");
        builder.AppendLine();
        builder.AppendLine("pub const OPERATIONS: &[OperationMetadata] = &[");
        foreach (var operation in schema.Operations)
        {
            builder.AppendLine("    OperationMetadata {");
            builder.AppendLine($"        id: OP_{UpperSnake(operation.Name)},");
            builder.AppendLine($"        name: \"{operation.Name}\",");
            builder.AppendLine($"        value_kind: ValueKind::{Pascal(operation.Value)},");
            builder.AppendLine(
                $"        required_capability: 0x{CapabilityBit(schema, operation.Requires):X16},"
            );
            builder.AppendLine("    },");
        }
        builder.AppendLine("];");
        builder.AppendLine();
        builder.AppendLine(
            "pub fn component_metadata(id: u16) -> Option<&'static ComponentMetadata> {"
        );
        builder.AppendLine("    COMPONENTS.iter().find(|metadata| metadata.id == id)");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine(
            "pub fn operation_metadata(id: u16) -> Option<&'static OperationMetadata> {"
        );
        builder.AppendLine("    OPERATIONS.iter().find(|metadata| metadata.id == id)");
        builder.AppendLine("}");
        builder.AppendLine();
        var payloadOperations = schema
            .Operations.Where(operation => operation.Payload == "event")
            .Select(operation => $"OP_{UpperSnake(operation.Name)}")
            .ToArray();
        builder.AppendLine("pub fn allows_payload(operation: u16) -> bool {");
        builder.AppendLine(
            payloadOperations.Length == 0
                ? "    false"
                : $"    matches!(operation, {string.Join(" | ", payloadOperations)})"
        );
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("fn f32_payload_in_range(a: u64, min: f32, max: f32) -> bool {");
        builder.AppendLine("    let value = f32::from_bits(a as u32);");
        builder.AppendLine("    value >= min && value <= max");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("fn valid_packed_table_column(a: u64) -> bool {");
        builder.AppendLine("    a >> 36 == 0");
        builder.AppendLine("        && (a >> 32) & 0b11 <= 1");
        builder.AppendLine("        && (a >> 34) & 0b11 <= 2");
        builder.AppendLine("        && f32::from_bits(a as u32).is_finite()");
        builder.AppendLine("        && f32::from_bits(a as u32) > 0.0");
        builder.AppendLine("        && ((a >> 32) & 0b11 != 1 || f32::from_bits(a as u32) <= 1.0)");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("pub fn payload_error(operation: u16, a: u64) -> i32 {");
        builder.AppendLine("    match operation {");
        foreach (
            var operation in schema.Operations.Where(operation => operation.Validation is not null)
        )
        {
            builder.AppendLine(
                $"        OP_{UpperSnake(operation.Name)} if !({RustValidationExpression(operation.Validation!)}) => {operation.Validation!.Status},"
            );
        }
        builder.AppendLine("        _ => 0,");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("#[cfg(test)]");
        builder.AppendLine("mod tests {");
        builder.AppendLine("    use super::*;");
        builder.AppendLine("    use std::collections::HashSet;");
        builder.AppendLine();
        builder.AppendLine("    #[test]");
        builder.AppendLine(
            "    fn generated_registry_has_unique_ids_and_resolvable_capabilities() {"
        );
        builder.AppendLine(
            "        let component_ids: HashSet<_> = COMPONENTS.iter().map(|item| item.id).collect();"
        );
        builder.AppendLine(
            "        let operation_ids: HashSet<_> = OPERATIONS.iter().map(|item| item.id).collect();"
        );
        builder.AppendLine("        assert_eq!(component_ids.len(), COMPONENTS.len());");
        builder.AppendLine("        assert_eq!(operation_ids.len(), OPERATIONS.len());");
        builder.AppendLine("        assert!(OPERATIONS.iter().all(|operation| {");
        builder.AppendLine("            COMPONENTS");
        builder.AppendLine("                .iter()");
        builder.AppendLine("                .any(|component| operation.applies_to(component.id))");
        builder.AppendLine("        }));");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    #[test]");
        builder.AppendLine("    fn value_kind_layout_matches_the_wire_contract() {");
        builder.AppendLine("        assert_eq!(ValueKind::None as u16, 0);");
        builder.AppendLine("        assert_eq!(ValueKind::F32 as u16, 1);");
        builder.AppendLine("        assert_eq!(ValueKind::U32 as u16, 2);");
        builder.AppendLine("        assert_eq!(ValueKind::Callback as u16, 3);");
        builder.AppendLine("        assert_eq!(ValueKind::U64 as u16, 4);");
        builder.AppendLine("        assert_eq!(ValueKind::F32x2 as u16, 5);");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

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

    private static void ValidateId(int id, string description)
    {
        if (id is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"The {description} ID must fit a non-zero ushort."
            );
        }
    }

    private static void ValidateName(string name, string kind)
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || !char.IsAsciiLetterLower(name[0])
            || name.Any(character =>
                !(
                    char.IsAsciiLetterLower(character)
                    || char.IsAsciiDigit(character)
                    || character == '_'
                )
            )
        )
        {
            throw new InvalidOperationException(
                $"The {kind} name '{name}' must be lower_snake_case ASCII."
            );
        }
    }

    private static void ValidateCSharpIdentifier(string name, string kind)
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || !(char.IsAsciiLetter(name[0]) || name[0] == '_')
            || name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_'))
        )
        {
            throw new InvalidOperationException(
                $"The {kind} C# name '{name}' is not a valid identifier."
            );
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string description)
        where T : notnull
    {
        var seen = new HashSet<T>();
        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                throw new InvalidOperationException($"Duplicate {description}: {value}");
            }
        }
    }

    private static string Pascal(string value) =>
        string.Concat(
            value
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
        );

    private static string UpperSnake(string value) => value.ToUpperInvariant();

    private static string CSharpValidationExpression(Validation validation) =>
        validation.Kind switch
        {
            "bool" => "a <= 1UL",
            "u32Min" => $"a >= {CSharpUnsignedLiteral(validation.Min!.Value)}",
            "u32Max" => $"a <= {CSharpUnsignedLiteral(validation.Max!.Value)}",
            "u32Range" =>
                $"a >= {CSharpUnsignedLiteral(validation.Min!.Value)} && a <= {CSharpUnsignedLiteral(validation.Max!.Value)}",
            "f32Min" =>
                $"BitConverter.UInt32BitsToSingle((uint)a) {(validation.MinExclusive ? ">" : ">=")} {CSharpFloatLiteral(validation.Min!.Value)}",
            "f32Range" =>
                $"BitConverter.UInt32BitsToSingle((uint)a) >= {CSharpFloatLiteral(validation.Min!.Value)} && BitConverter.UInt32BitsToSingle((uint)a) <= {CSharpFloatLiteral(validation.Max!.Value)}",
            "packedTableColumn" =>
                "(a >> 36) == 0 && ((a >> 32) & 0b11) <= 1 && ((a >> 34) & 0b11) <= 2 && float.IsFinite(BitConverter.UInt32BitsToSingle((uint)a)) && BitConverter.UInt32BitsToSingle((uint)a) > 0f && (((a >> 32) & 0b11) != 1 || BitConverter.UInt32BitsToSingle((uint)a) <= 1f)",
            _ => throw new InvalidOperationException(
                $"Unknown validation kind '{validation.Kind}'."
            ),
        };

    private static string RustValidationExpression(Validation validation) =>
        validation.Kind switch
        {
            "bool" => "a <= 1",
            "u32Min" => $"a >= {RustUnsignedLiteral(validation.Min!.Value)}",
            "u32Max" => $"a <= {RustUnsignedLiteral(validation.Max!.Value)}",
            "u32Range" =>
                $"a >= {RustUnsignedLiteral(validation.Min!.Value)} && a <= {RustUnsignedLiteral(validation.Max!.Value)}",
            "f32Min" =>
                $"f32::from_bits(a as u32) {(validation.MinExclusive ? ">" : ">=")} {RustFloatLiteral(validation.Min!.Value)}",
            "f32Range" =>
                $"f32_payload_in_range(a, {RustFloatLiteral(validation.Min!.Value)}, {RustFloatLiteral(validation.Max!.Value)})",
            "packedTableColumn" => "valid_packed_table_column(a)",
            _ => throw new InvalidOperationException(
                $"Unknown validation kind '{validation.Kind}'."
            ),
        };

    private static string CSharpUnsignedLiteral(double value) => $"{checked((ulong)value)}UL";

    private static string RustUnsignedLiteral(double value) => $"{checked((ulong)value)}u64";

    private static string CSharpFloatLiteral(double value) =>
        $"{value.ToString("R", CultureInfo.InvariantCulture)}f";

    private static string RustFloatLiteral(double value) =>
        $"{value.ToString("R", CultureInfo.InvariantCulture)}f32";

    private static string Normalize(string value) => value.Replace("\r\n", "\n");
}

internal sealed record BindingSchema(
    int SchemaVersion,
    List<string> Capabilities,
    List<Component> Components,
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
    string Configuration,
    Dictionary<string, int> Flags,
    Dictionary<string, ExtensionCommand> Commands
);

internal sealed record ExtensionCommand(ushort Id, string Payload, string Revision);

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

internal sealed record Operation(
    int Id,
    string Name,
    [property: JsonPropertyName("csharp")] string CSharp,
    string Value,
    string Requires,
    string? ManagedApi,
    string? Payload,
    Validation? Validation
);

internal sealed record Validation(
    string Kind,
    double? Min,
    double? Max,
    bool MinExclusive,
    int Status
);
