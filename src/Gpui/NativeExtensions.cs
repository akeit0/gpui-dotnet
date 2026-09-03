using System.Diagnostics;
using System.Text;

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

/// <summary>
/// Extension-neutral imperative route to one retained native extension resource. Typed extension
/// packages wrap this value and define their own command IDs and payload formats.
/// </summary>
[DebuggerDisplay("{DebuggerView,nq}")]
public readonly struct NativeExtensionController
{
    private readonly ViewBase? _owner;
    private readonly NativeExtensionComponent _component;
    private readonly byte[]? _utf8ExtensionId;
    private readonly byte[]? _utf8ComponentKind;
    private readonly byte[]? _utf8Key;

    internal NativeExtensionController(
        ViewBase owner,
        NativeExtensionComponent component,
        string key
    )
        : this(owner, component, Encoding.UTF8.GetBytes(key)) { }

    internal NativeExtensionController(
        ViewBase owner,
        NativeExtensionComponent component,
        ReadOnlySpan<byte> utf8Key
    )
        : this(owner, component, utf8Key.ToArray()) { }

    private NativeExtensionController(
        ViewBase owner,
        NativeExtensionComponent component,
        byte[] utf8Key
    )
    {
        _owner = owner;
        _component = component;
        _utf8ExtensionId = Encoding.UTF8.GetBytes(component.Extension.Id);
        _utf8ComponentKind = Encoding.UTF8.GetBytes(component.Kind);
        _utf8Key = utf8Key;
    }

    /// <summary>True when this controller has a mounted View and retained resource identity.</summary>
    public bool IsBound => _owner is not null;

    /// <summary>
    /// Dispatches an extension-defined command. The native entry point copies the payload before
    /// returning, so the caller may reuse its source memory immediately.
    /// </summary>
    public void Dispatch(
        ushort command,
        ReadOnlySpan<byte> payload = default,
        ushort flags = 0,
        ulong expectedRevision = 0
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(command);
        Owner.DispatchNativeExtensionCommand(
            _component.Extension.Version,
            _component.Extension.SchemaHash,
            ExtensionId,
            ComponentKind,
            Key,
            command,
            flags,
            expectedRevision,
            payload
        );
    }

    internal NativeExtensionComponent Component => _component;
    internal ReadOnlySpan<byte> Key => _utf8Key;

    internal void ValidateOwner(ViewBase owner)
    {
        if (_owner is null || !ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException(
                "A native extension controller can only declare its resource from the owning View."
            );
        }
    }

    private ViewBase Owner =>
        _owner
        ?? throw new InvalidOperationException("Default NativeExtensionController cannot be used.");
    private ReadOnlySpan<byte> ExtensionId =>
        _utf8ExtensionId
        ?? throw new InvalidOperationException("Default NativeExtensionController cannot be used.");
    private ReadOnlySpan<byte> ComponentKind =>
        _utf8ComponentKind
        ?? throw new InvalidOperationException("Default NativeExtensionController cannot be used.");

    private string DebuggerView =>
        _utf8Key is null
            ? "unbound"
            : $"{_component.Extension.Id}/{_component.Kind} ({_utf8Key.Length}-byte key)";
}

/// <summary>An owned, extension-neutral native event packet.</summary>
public sealed class NativeExtensionEvent
{
    private readonly byte[] _payload;

    internal NativeExtensionEvent(ushort kind, ushort flags, ulong revision, byte[] payload)
    {
        Kind = kind;
        Flags = flags;
        Revision = revision;
        _payload = payload;
    }

    public ushort Kind { get; }
    public ushort Flags { get; }
    public ulong Revision { get; }
    public ReadOnlyMemory<byte> Payload => _payload;
}

/// <summary>Implemented by typed extension events decoded from the generic native packet.</summary>
public interface INativeExtensionEvent<TSelf>
    where TSelf : INativeExtensionEvent<TSelf>
{
    static abstract TSelf Decode(NativeExtensionEvent nativeEvent);
}

/// <summary>Render-bound token for one typed extension event callback.</summary>
public readonly struct NativeExtensionEventBinding
{
    internal NativeExtensionEventBinding(ulong token) => Token = token;

    public ulong Token { get; }
}
