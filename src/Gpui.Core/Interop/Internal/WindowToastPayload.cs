using System.Buffers.Binary;
using System.Text;

namespace Gpui.Interop.Internal;

internal static class WindowToastPayload
{
    internal const int MaxIdBytes = 256;
    internal const int MaxTitleBytes = 4096;
    internal const int MaxDescriptionBytes = 16384;
    private const int HeaderSize = 24;
    private const uint DefaultTimeoutMilliseconds = 5000;
    private const uint MaxTimeoutMilliseconds = 86_400_000;

    internal static byte[] Encode(GpuiToast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        var id = Encoding.UTF8.GetBytes(
            toast.Id ?? throw new ArgumentException("Toast ID is required.", nameof(toast))
        );
        var title = Encoding.UTF8.GetBytes(
            toast.Title ?? throw new ArgumentException("Toast title is required.", nameof(toast))
        );
        var description = Encoding.UTF8.GetBytes(toast.Description ?? string.Empty);
        if (id.Length is < 1 or > MaxIdBytes)
            throw new ArgumentException("Toast ID must contain 1–256 UTF-8 bytes.", nameof(toast));
        if (title.Length is < 1 or > MaxTitleBytes)
            throw new ArgumentException(
                "Toast title must contain 1–4096 UTF-8 bytes.",
                nameof(toast)
            );
        if (description.Length > MaxDescriptionBytes)
            throw new ArgumentException(
                "Toast description exceeds 16384 UTF-8 bytes.",
                nameof(toast)
            );

        var timeout = DefaultTimeoutMilliseconds;
        if (toast.Timeout is { } requested)
        {
            var milliseconds = Math.Ceiling(requested.TotalMilliseconds);
            if (
                !double.IsFinite(milliseconds)
                || milliseconds < 0
                || milliseconds > MaxTimeoutMilliseconds
            )
                throw new ArgumentOutOfRangeException(
                    nameof(toast),
                    "Toast timeout must be between zero and one day."
                );
            timeout = (uint)milliseconds;
        }

        var payload = new byte[HeaderSize + id.Length + title.Length + description.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), timeout);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), (uint)id.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), (uint)title.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), (uint)description.Length);
        id.CopyTo(payload.AsSpan(HeaderSize));
        title.CopyTo(payload.AsSpan(HeaderSize + id.Length));
        description.CopyTo(payload.AsSpan(HeaderSize + id.Length + title.Length));
        return payload;
    }

    internal static void ValidateId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var count = Encoding.UTF8.GetByteCount(id);
        if (count is < 1 or > MaxIdBytes)
            throw new ArgumentException("Toast ID must contain 1–256 UTF-8 bytes.", nameof(id));
    }
}
