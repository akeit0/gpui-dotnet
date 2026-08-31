namespace Gpui;

/// <summary>
/// Stable identity for a managed child slot. Keys belong to the parent view; they are not global.
/// Use keys when child order can change, for routing/tab content, or for dynamic collections.
/// </summary>
public readonly struct ChildKey : IEquatable<ChildKey>
{
    private readonly KeyKind _kind;
    private readonly ulong _low;
    private readonly ulong _high;
    private readonly string? _text;

    private ChildKey(KeyKind kind, ulong low, ulong high = 0, string? text = null)
    {
        _kind = kind;
        _low = low;
        _high = high;
        _text = text;
    }

    public ChildKey(long value)
        : this(KeyKind.Int64, unchecked((ulong)value)) { }

    public ChildKey(ulong value)
        : this(KeyKind.UInt64, value) { }

    public ChildKey(string value)
        : this(KeyKind.String, 0, 0, value ?? throw new ArgumentNullException(nameof(value)))
    {
        if (value.Length == 0)
        {
            throw new ArgumentException("A child key cannot be empty.", nameof(value));
        }
    }

    public ChildKey(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        _kind = KeyKind.Guid;
        _low = BitConverter.ToUInt64(bytes[..8]);
        _high = BitConverter.ToUInt64(bytes[8..]);
        _text = null;
    }

    public static implicit operator ChildKey(string value) => new(value);

    public static implicit operator ChildKey(int value) => new((long)value);

    public static implicit operator ChildKey(uint value) => new((ulong)value);

    public static implicit operator ChildKey(long value) => new(value);

    public static implicit operator ChildKey(ulong value) => new(value);

    public static implicit operator ChildKey(Guid value) => new(value);

    public bool Equals(ChildKey other) =>
        _kind == other._kind
        && _low == other._low
        && _high == other._high
        && (_kind != KeyKind.String || string.Equals(_text, other._text, StringComparison.Ordinal));

    public override bool Equals(object? obj) => obj is ChildKey other && Equals(other);

    public override int GetHashCode() =>
        _kind == KeyKind.String
            ? HashCode.Combine((byte)_kind, StringComparer.Ordinal.GetHashCode(_text!))
            : HashCode.Combine((byte)_kind, _low, _high);

    public override string ToString() =>
        _kind switch
        {
            KeyKind.Int64 => unchecked((long)_low).ToString(),
            KeyKind.UInt64 => _low.ToString(),
            KeyKind.String => _text!,
            KeyKind.Guid => new Guid(ToGuidBytes()).ToString(),
            _ => "<invalid>",
        };

    internal bool IsValid => _kind != KeyKind.None;

    private byte[] ToGuidBytes()
    {
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 8), _low);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 8), _high);
        return bytes;
    }

    private enum KeyKind : byte
    {
        None,
        Int64,
        UInt64,
        String,
        Guid,
    }
}

internal readonly struct ChildSlot : IEquatable<ChildSlot>
{
    private readonly ChildSlotKind _kind;
    private readonly uint _position;
    private readonly ChildKey _key;

    private ChildSlot(ChildSlotKind kind, uint position, ChildKey key)
    {
        _kind = kind;
        _position = position;
        _key = key;
    }

    internal static ChildSlot Auto => new(ChildSlotKind.Auto, 0, default);

    internal static ChildSlot Positional(uint position) =>
        new(ChildSlotKind.Positional, position, default);

    internal static ChildSlot Keyed(ChildKey key)
    {
        if (!key.IsValid)
        {
            throw new ArgumentException("A keyed child requires a non-default key.", nameof(key));
        }
        return new ChildSlot(ChildSlotKind.Keyed, 0, key);
    }

    internal bool IsAuto => _kind == ChildSlotKind.Auto;

    public bool Equals(ChildSlot other) =>
        _kind == other._kind
        && (_kind == ChildSlotKind.Keyed ? _key.Equals(other._key) : _position == other._position);

    public override bool Equals(object? obj) => obj is ChildSlot other && Equals(other);

    public override int GetHashCode() =>
        _kind == ChildSlotKind.Keyed
            ? HashCode.Combine((byte)_kind, _key)
            : HashCode.Combine((byte)_kind, _position);

    public override string ToString() =>
        _kind switch
        {
            ChildSlotKind.Auto => "auto",
            ChildSlotKind.Positional => $"position:{_position}",
            ChildSlotKind.Keyed => $"key:{_key}",
            _ => "<invalid>",
        };

    private enum ChildSlotKind : byte
    {
        Auto,
        Positional,
        Keyed,
    }
}
