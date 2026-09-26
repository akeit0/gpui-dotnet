using System.Buffers.Binary;
using System.Text;

namespace Gpui.Interop.Internal;

internal static class WindowOpenPayload
{
    private const int HeaderSize = 20;
    internal const int MaxTitleBytes = 4096;

    internal static byte[] Encode(string title, float minimumWidth, float minimumHeight)
    {
        var titleBytes = Encoding.UTF8.GetBytes(title);
        if (titleBytes.Length is < 1 or > MaxTitleBytes)
            throw new ArgumentException(
                "Window title must contain 1–4096 UTF-8 bytes.",
                nameof(title)
            );

        var payload = new byte[HeaderSize + titleBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(4, 4), minimumWidth);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8, 4), minimumHeight);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), (uint)titleBytes.Length);
        titleBytes.CopyTo(payload.AsSpan(HeaderSize));
        return payload;
    }
}
