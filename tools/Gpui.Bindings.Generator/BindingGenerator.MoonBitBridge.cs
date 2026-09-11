using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class BindingGenerator
{
    private sealed record BridgeSchema(int Version, Dictionary<string, int> Constants, List<BridgeFunction> Functions);
    private sealed record BridgeFunction(string Name, string Result, List<BridgeParameter> Parameters);
    private sealed record BridgeParameter(string Name, string Type);

    private static (string C, string Moon) BridgeType(string type) => type switch
    {
        "void" => ("void", "Unit"),
        "i32" => ("int32_t", "Int"),
        "u32" => ("uint32_t", "UInt"),
        "u64" => ("uint64_t", "UInt64"),
        "f32" => ("float", "Float"),
        "bytes" => ("const uint8_t *", "Bytes"),
        "buffer" => ("uint8_t *", "FixedArray[Byte]"),
        "host" => ("void *", "Host"),
        "request" => ("void *", "Request"),
        "dispatch" => ("GpuiMoonDispatch", "FuncRef[(Int, UInt64, UInt64, UInt64, UInt, UInt, Int, Request) -> Int]"),
        _ => throw new InvalidOperationException($"Unknown bridge type '{type}'."),
    };

    private static void AddMoonBitBridgeOutputs(string root, Dictionary<string, string> outputs)
    {
        var schema = JsonSerializer.Deserialize<BridgeSchema>(
            File.ReadAllText(Path.Combine(root, "bindings/moonbit/bridge.json")), JsonOptions
        ) ?? throw new InvalidOperationException("Missing MoonBit bridge schema.");
        if (schema.Version != 1)
            throw new InvalidOperationException("Unsupported MoonBit bridge schema version.");
        var moon = new StringBuilder().AppendLine(MoonHeader).AppendLine();
        foreach (var name in new[] { "Host", "Request" })
        {
            moon.AppendLine("///|").AppendLine("#external").AppendLine($"pub type {name}").AppendLine();
        }
        var c = new StringBuilder().AppendLine(MoonHeader)
            .AppendLine("#ifndef GPUI_MOON_BRIDGE_G_H").AppendLine("#define GPUI_MOON_BRIDGE_G_H")
            .AppendLine("#include <stdint.h>")
            .AppendLine("typedef int32_t (*GpuiMoonDispatch)(int32_t, uint64_t, uint64_t, uint64_t, uint32_t, uint32_t, int32_t, void *);");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in schema.Constants)
        {
            if (!Regex.IsMatch(name, "^[A-Z][A-Z0-9_]*$") || !names.Add(name))
                throw new InvalidOperationException($"Invalid bridge constant '{name}'.");
            MoonConstant(moon, name, "Int", value.ToString());
            c.AppendLine($"#define GPUI_MOON_{name} ({value})");
        }
        foreach (var function in schema.Functions)
        {
            if (!Regex.IsMatch(function.Name, "^[a-z][a-z0-9_]*$") || !names.Add(function.Name))
                throw new InvalidOperationException($"Invalid or duplicate bridge function '{function.Name}'.");
            var parameters = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in function.Parameters)
            {
                if (!Regex.IsMatch(parameter.Name, "^[a-z][a-z0-9_]*$") || !parameters.Add(parameter.Name)
                    || parameter.Type == "void")
                    throw new InvalidOperationException($"Invalid parameter in '{function.Name}'.");
                _ = BridgeType(parameter.Type);
            }
            if (function.Result is "bytes" or "buffer" or "dispatch")
                throw new InvalidOperationException("Bridge resources must have explicit allocation and copying APIs.");
            var borrow = function.Parameters.Where(p => p.Type is "bytes" or "buffer").Select(p => p.Name).ToArray();
            moon.AppendLine("///|");
            if (borrow.Length != 0)
                moon.AppendLine($"#borrow({string.Join(", ", borrow)})");
            moon.AppendLine($"pub extern \"C\" fn {function.Name}(");
            foreach (var parameter in function.Parameters)
                moon.AppendLine($"  {parameter.Name} : {BridgeType(parameter.Type).Moon},");
            moon.AppendLine($") -> {BridgeType(function.Result).Moon} = \"gpui_moon_{function.Name}\"").AppendLine();
            var cParameters = function.Parameters.Count == 0 ? "void" : string.Join(", ",
                function.Parameters.Select(p => $"{BridgeType(p.Type).C} {p.Name}"));
            c.AppendLine($"{BridgeType(function.Result).C} gpui_moon_{function.Name}({cParameters});");
        }
        c.AppendLine("#endif");
        outputs.Add("moonbit/internal/ffi/ffi.g.mbt", moon.ToString().TrimEnd() + "\n");
        outputs.Add("moonbit/internal/ffi/bridge.g.h", c.ToString().TrimEnd() + "\n");
    }
}
