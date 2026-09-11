using System.Text;

internal static partial class BindingGenerator
{
    private sealed record GeneratorOptions(string Command, string Root, HashSet<string> Targets);

    private static readonly string[] TargetNames = ["csharp", "rust", "moonbit", "reference"];

    private static GeneratorOptions ParseOptions(string[] args)
    {
        var command = "generate";
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var rootSeen = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "generate" or "verify" or "list" when index == 0:
                    command = args[index];
                    break;
                case "--root" when !rootSeen:
                    rootSeen = true;
                    if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("--root requires a repository path.");
                    }
                    break;
                case "--target":
                    if (++index == args.Length)
                    {
                        throw new InvalidOperationException("--target requires a target name.");
                    }
                    foreach (var target in args[index].Split(','))
                    {
                        if (target != "all" && !TargetNames.Contains(target, StringComparer.Ordinal))
                        {
                            throw new InvalidOperationException($"Unknown generation target '{target}'.");
                        }
                        targets.Add(target);
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "Usage: Gpui.Bindings.Generator [generate|verify|list] "
                            + "[--root <repository>] [--target all|csharp|rust|moonbit|reference]"
                    );
            }
        }
        if (targets.Count == 0 || targets.Contains("all"))
        {
            targets.UnionWith(TargetNames);
        }
        return new GeneratorOptions(command, GetRoot(args), targets);
    }

    private static string OutputTarget(string path) =>
        path.EndsWith(".cs", StringComparison.Ordinal) ? "csharp"
        : path.EndsWith(".rs", StringComparison.Ordinal) ? "rust"
        : path.StartsWith("moonbit/", StringComparison.Ordinal) ? "moonbit"
        : "reference";

    private static string SafeOutputPath(string root, string relative)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
            || relative.Contains('\\') || relative.Contains(':')
            || relative.Split('/').Any(part => part is "" or ".." or "."))
        {
            throw new InvalidOperationException($"Output must be a repository-relative path: '{relative}'.");
        }
        var absolute = Path.GetFullPath(Path.Combine(root, relative));
        var rel = Path.GetRelativePath(root, absolute);
        if (rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Output escapes the repository: '{relative}'.");
        }
        // A generated file must never follow an existing symlink out of the checkout.
        for (var cursor = absolute; cursor != Path.GetFullPath(root); cursor = Path.GetDirectoryName(cursor)!)
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor))
                && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Generated output traverses a link: '{relative}'.");
            }
        }
        return absolute;
    }

    private static void WriteGeneratedFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
