using System.Buffers.Binary;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ListActivationDecodesOwnedIdentityAndOptionalRevision(bool keyboard, bool identity)
    {
        var view = new ListActivationProbe();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        var payload = ActivationPayload(51, identity ? ulong.MaxValue : 0);
        Assert.Equal(0, fixture.Control(view.Token, (ushort)ListEventKind.Activated, payload,
            (ushort)((keyboard ? 1 : 0) | (identity ? 2 : 0)), identity ? ulong.MaxValue : 0));
        payload.AsSpan().Clear();
        Assert.Equal(new ListActivationEvent(51, identity ? ulong.MaxValue : null,
            identity ? ulong.MaxValue : null, keyboard ? ListActivationSource.Keyboard : ListActivationSource.Pointer), view.Received);
    }

    [Fact]
    public void ListActivationAcceptsExplicitZeroRevisionAndRejectsMalformedPackets()
    {
        var view = new ListActivationProbe();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        var payload = ActivationPayload(0, 0);
        Assert.Equal(0, fixture.Control(view.Token, (ushort)ListEventKind.Activated, payload, 2, 0));
        Assert.Equal(0UL, view.Received!.Value.ContentRevision);
        view.Received = null;
        Assert.Equal(-112, fixture.Control(view.Token, (ushort)ListEventKind.Activated, payload, 4));
        Assert.Equal(-112, fixture.Control(view.Token, (ushort)ListEventKind.Activated, payload, 0, 1));
        Assert.Equal(-112, fixture.Control(view.Token, (ushort)ListEventKind.Activated, payload.AsSpan(1)));
        Assert.Equal(-112, fixture.Control(view.Token, (ushort)ListEventKind.Activated, ActivationPayload(uint.MaxValue, 1)));
        payload[4] = 1;
        Assert.Equal(-112, fixture.Control(view.Token, (ushort)ListEventKind.Activated, payload));
        Assert.Null(view.Received);
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void ListActivationFailureUsesTheNormalManagedEventBoundary()
    {
        var view = new ListActivationProbe { ThrowOnActivation = true };
        using var fixture = new SessionFixture(view);
        fixture.Render();
        Assert.Equal(-113, fixture.Control(view.Token, (ushort)ListEventKind.Activated, ActivationPayload(1, 5)));
        Assert.Equal("activation failed", fixture.Session.Failure!.Message);
    }

    private static byte[] ActivationPayload(uint index, ulong itemId)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, index);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), itemId);
        return bytes;
    }

    private sealed class ListActivationProbe : ProbeView
    {
        internal ulong Token;
        internal ListActivationEvent? Received;
        internal bool ThrowOnActivation;

        protected override Element Render(ref RenderContext ui)
        {
            Token = Runtime.Events.BindListActivation<ListActivationProbe>(static (view, value) =>
            {
                if (view.ThrowOnActivation) throw new InvalidOperationException("activation failed");
                view.Received = value;
            });
            return base.Render(ref ui);
        }
    }
}
