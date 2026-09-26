using System.Buffers.Binary;
using System.Text;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed class WindowOpenPayloadTests
{
    [Fact]
    public void EncodesVersionedMinimumAndUtf8Title()
    {
        var payload = WindowOpenPayload.Encode("東京", 480, 320);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4)));
        Assert.Equal(480f, BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(4, 4)));
        Assert.Equal(320f, BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(8, 4)));
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(12, 4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(16, 4)));
        Assert.Equal("東京", Encoding.UTF8.GetString(payload.AsSpan(20)));
    }

    [Fact]
    public void RejectsOversizedTitle()
    {
        Assert.Throws<ArgumentException>(() =>
            WindowOpenPayload.Encode(new string('x', 4097), 480, 320)
        );
    }
}
