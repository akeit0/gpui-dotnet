namespace Gpui;

// Shared resource-command wire types. The per-resource controllers and option structs live in
// Scrolling.cs, Lists.cs, Inputs.cs, and Tables.cs.

/// <summary>
/// Auto resource-key encoding for ref-bound controllers. The key is the 0x01 namespace byte
/// followed by the 64-bit id in canonical big-endian base-127 digits, each stored as digit + 1
/// (0x01..0x7F, no leading zero digits, id 0 stored as a single zero digit). Every byte is
/// single-byte valid UTF-8 and the sequence never contains NUL, so it flows through the
/// existing UTF-8 key channel unchanged. The encoding is minimal-length and injective, and
/// explicit keys reject control characters at the managed boundary, which makes the two key
/// spaces disjoint by construction. Nothing on the wire path decodes keys; the debugger view
/// does (<see cref="TryDecodeAutoKey"/>).
/// </summary>
internal static class ResourceKeys
{
    public const byte AutoKeyPrefix = 0x01;
    private const ulong DigitBase = 127;
    private const int MaxDigits = 10; // 127^10 > 2^64

    public static byte[] EncodeAutoKey(ulong id)
    {
        Span<byte> digits = stackalloc byte[MaxDigits];
        var count = 0;
        do
        {
            digits[count++] = (byte)(id % DigitBase + 1);
            id /= DigitBase;
        } while (id != 0);

        var key = new byte[1 + count];
        key[0] = AutoKeyPrefix;
        for (var i = 0; i < count; i++)
        {
            key[1 + i] = digits[count - 1 - i];
        }
        return key;
    }

    /// <summary>Debugger/diagnostics decode. Accepts non-canonical encodings.</summary>
    public static bool TryDecodeAutoKey(ReadOnlySpan<byte> key, out ulong id)
    {
        id = 0;
        if (key.IsEmpty || key[0] != AutoKeyPrefix)
        {
            return false;
        }
        foreach (var b in key.Slice(1))
        {
            if (b == 0 || b > (byte)DigitBase)
            {
                id = 0;
                return false;
            }
            id = id * DigitBase + (ulong)(b - 1);
        }
        return true;
    }

    /// <summary>
    /// Explicit keys reject C0 control characters. Besides being a sane identifier rule, this
    /// guarantees an explicit key can never start with the 0x01 auto-key namespace byte.
    /// </summary>
    public static void ValidateExplicitChars(ReadOnlySpan<char> key, string paramName)
    {
        foreach (var c in key)
        {
            if (c < ' ')
            {
                throw new ArgumentException(
                    "An explicit resource key cannot contain control characters.",
                    paramName
                );
            }
        }
    }

    public static void ValidateExplicitBytes(ReadOnlySpan<byte> key, string paramName)
    {
        foreach (var b in key)
        {
            if (b < 0x20)
            {
                throw new ArgumentException(
                    "An explicit resource key cannot contain control characters.",
                    paramName
                );
            }
        }
    }
}

internal enum ResourceKind : ushort
{
    Scroll = 1,
    List = 2,
    Input = 3,
    Slider = 4,
    Dock = 5,
}

internal enum ResourceCommandKind : ushort
{
    ScrollToOffset = 1,
    ScrollToTop = 2,
    ScrollToBottom = 3,
    ListScrollToItem = 10,
    ListSplice = 11,
    ListReset = 12,
    ListRefresh = 13,
    InputFocus = 20,
    InputBlur = 21,
    InputSetValue = 22,
    InputSelectAll = 23,
    SliderSetValue = 30,
    DockClosePanel = 40,
    DockSetRegionOpen = 41,
    DockImportLayout = 42,
    DockExportLayout = 43,
}

internal readonly record struct ResourceCommand(
    ResourceKind ResourceKind,
    ResourceCommandKind Command,
    string? Key,
    ulong A,
    ulong B,
    string? Data = null,
    byte[]? Utf8Key = null
);
