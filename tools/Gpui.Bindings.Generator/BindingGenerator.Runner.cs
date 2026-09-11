using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static partial class BindingGenerator
{
    private const string SchemaPath = "bindings/schema.json";

    private const string ExtensionManifestPath = "bindings/extensions.json";

    private const string CSharpProtocolOutputPath = "src/Gpui/Rendering/Semantic.g.cs";

    private const string CSharpElementsOutputPath = "src/Gpui/Rendering/SemanticElements.g.cs";

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
            var options = ParseOptions(args);
            var command = options.Command;
            var root = options.Root;
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
                [CSharpProtocolOutputPath] = GenerateCSharpProtocol(schema, hash),
                [CSharpElementsOutputPath] = GenerateCSharpElements(schema),
                [RustOutputPath] = GenerateRust(schema, hash),
                ["docs/SEMANTIC_IDS.md"] = GenerateIdReference(schema),
                ["moonbit/protocol/semantic.g.mbt"] = GenerateMoonBitProtocol(schema, hash),
                ["moonbit/elements.g.mbt"] = GenerateMoonBitElements(schema),
            };

            AddMoonBitBridgeOutputs(root, outputs);

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
                if (generation.MoonbitOutput is { } moonbitOutput)
                {
                    if (!moonbitOutput.StartsWith("moonbit/", StringComparison.Ordinal)
                        || !moonbitOutput.EndsWith(".g.mbt", StringComparison.Ordinal)
                        || !outputs.TryAdd(moonbitOutput, GenerateMoonBitExtension(extensionSchema, extensionHash)))
                    {
                        throw new InvalidOperationException($"Invalid or duplicate MoonBit extension output '{moonbitOutput}'.");
                    }
                }
            }

            // Validate every destination before filtering or writing any output. A target selection
            // limits writes, never validation or the schema/hash used by another frontend.
            foreach (var path in outputs.Keys)
            {
                _ = SafeOutputPath(root, path);
            }
            outputs = outputs.Where(output => options.Targets.Contains(OutputTarget(output.Key)))
                .ToDictionary(output => output.Key, output => output.Value, StringComparer.OrdinalIgnoreCase);
            if (command == "list")
            {
                foreach (var path in outputs.Keys.Order(StringComparer.Ordinal))
                {
                    Console.WriteLine($"{OutputTarget(path)}\t{path}");
                }
                return 0;
            }

            // Generated outputs use explicit LF line endings on every platform:
            // StringBuilder.AppendLine emits Environment.NewLine, so without this a Windows
            // checkout would rewrite every generated file with CRLF.
            foreach (var path in outputs.Keys.ToArray())
            {
                outputs[path] = Normalize(outputs[path]);
            }

            var stale = outputs
                .Where(output =>
                    !File.Exists(Path.Combine(root, output.Key))
                    || File.ReadAllText(Path.Combine(root, output.Key), Encoding.UTF8)
                        != output.Value
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
                WriteGeneratedFile(absolutePath, outputs[path]);
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

    private static string Normalize(string value) => value.Replace("\r\n", "\n");
}
