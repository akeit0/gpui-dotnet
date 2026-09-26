using System.Buffers.Binary;
using System.Text;
using Gpui;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed class WindowToastPayloadTests
{
    [Fact]
    public void EncodesVersionedUtf8FieldsAndPersistentTimeout()
    {
        var payload = WindowToastPayload.Encode(
            new GpuiToast("save", "保存", "Ready", TimeSpan.Zero)
        );
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4)));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(8, 4)));
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(12, 4)));
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(16, 4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(20, 4)));
        Assert.Equal("save保存Ready", Encoding.UTF8.GetString(payload.AsSpan(24)));
    }

    [Fact]
    public void RejectsOversizedFieldsAndTimeout()
    {
        Assert.Throws<ArgumentException>(() =>
            WindowToastPayload.Encode(new GpuiToast("", "Title"))
        );
        Assert.Throws<ArgumentException>(() =>
            WindowToastPayload.Encode(new GpuiToast("id", new string('x', 4097)))
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowToastPayload.Encode(new GpuiToast("id", "Title", Timeout: TimeSpan.FromDays(2)))
        );
    }
}
