namespace Gpui;

/// <summary>
/// Identifies one independently versioned managed/native extension schema.
/// </summary>
public readonly record struct NativeExtensionRequirement
{
    public NativeExtensionRequirement(string id, uint version, ulong schemaHash)
    {
        ValidateIdentifier(id, nameof(id));
        ArgumentOutOfRangeException.ThrowIfZero(version);
        ArgumentOutOfRangeException.ThrowIfZero(schemaHash);

        Id = id;
        Version = version;
        SchemaHash = schemaHash;
    }

    /// <summary>A stable, package-owned identifier such as <c>gpui.net.editor</c>.</summary>
    public string Id { get; }

    /// <summary>The extension protocol version understood by its managed schema.</summary>
    public uint Version { get; }

    /// <summary>A deterministic hash of the extension-specific semantic schema.</summary>
    public ulong SchemaHash { get; }

    internal void Validate(string paramName)
    {
        if (Id is null)
        {
            throw new ArgumentException("An extension requirement cannot be default.", paramName);
        }
        ValidateIdentifier(Id, paramName);
        if (Version == 0 || SchemaHash == 0)
        {
            throw new ArgumentException(
                "An extension requirement needs a non-zero version and schema hash.",
                paramName
            );
        }
    }

    internal static void ValidateIdentifier(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        if (value.Length > 127)
        {
            throw new ArgumentException(
                "An extension identifier cannot exceed 127 characters.",
                paramName
            );
        }
        foreach (var character in value)
        {
            if (
                character is not (
                    >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '.'
                    or '-'
                    or '_'
                )
            )
            {
                throw new ArgumentException(
                    "Extension identifiers may contain only ASCII letters, digits, '.', '-', and '_'.",
                    paramName
                );
            }
        }
    }
}

/// <summary>Identifies one component kind inside an extension schema.</summary>
public readonly record struct NativeExtensionComponent
{
    public NativeExtensionComponent(NativeExtensionRequirement extension, string kind)
    {
        extension.Validate(nameof(extension));
        NativeExtensionRequirement.ValidateIdentifier(kind, nameof(kind));
        Extension = extension;
        Kind = kind;
    }

    public NativeExtensionRequirement Extension { get; }
    public string Kind { get; }

    internal void Validate(string paramName)
    {
        Extension.Validate(paramName);
        if (Kind is null)
        {
            throw new ArgumentException("An extension component cannot be default.", paramName);
        }
        NativeExtensionRequirement.ValidateIdentifier(Kind, paramName);
    }
}
