using System.Buffers.Binary;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ListSelectionDecodesOwnedIdentityAndOptionalRevision(bool keyboard, bool identity)
    {
        var view = new ListSelectionProbe();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        var payload = SelectionPayload(51, identity ? ulong.MaxValue : 0);
        Assert.Equal(
            0,
            fixture.Control(
                view.Token,
                (ushort)ListEventKind.SelectionRequested,
                payload,
                (ushort)((keyboard ? 1 : 0) | (identity ? 2 : 0)),
                identity ? ulong.MaxValue : 0
            )
        );
        payload.AsSpan().Clear();
        Assert.Equal(
            new ListSelectionEvent(
                51,
                identity ? ulong.MaxValue : null,
                identity ? ulong.MaxValue : null,
                keyboard ? ListSelectionSource.Keyboard : ListSelectionSource.Pointer
            ),
            view.Received
        );
    }

    [Fact]
    public void ListSelectionAcceptsExplicitZeroRevisionAndRejectsMalformedPackets()
    {
        var view = new ListSelectionProbe();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        var payload = SelectionPayload(0, 0);
        Assert.Equal(
            0,
            fixture.Control(view.Token, (ushort)ListEventKind.SelectionRequested, payload, 2, 0)
        );
        Assert.Equal(0UL, view.Received!.Value.ContentRevision);
        view.Received = null;
        Assert.Equal(
            -112,
            fixture.Control(view.Token, (ushort)ListEventKind.SelectionRequested, payload, 4)
        );
        Assert.Equal(
            -112,
            fixture.Control(view.Token, (ushort)ListEventKind.SelectionRequested, payload, 0, 1)
        );
        Assert.Equal(
            -112,
            fixture.Control(view.Token, (ushort)ListEventKind.SelectionRequested, payload.AsSpan(1))
        );
        Assert.Equal(
            -112,
            fixture.Control(
                view.Token,
                (ushort)ListEventKind.SelectionRequested,
                SelectionPayload(uint.MaxValue, 1)
            )
        );
        payload[4] = 1;
        Assert.Equal(
            -112,
            fixture.Control(view.Token, (ushort)ListEventKind.SelectionRequested, payload)
        );
        Assert.Null(view.Received);
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void ListSelectionFailureUsesTheNormalManagedEventBoundary()
    {
        var view = new ListSelectionProbe { ThrowOnSelection = true };
        using var fixture = new SessionFixture(view);
        fixture.Render();
        Assert.Equal(
            -113,
            fixture.Control(
                view.Token,
                (ushort)ListEventKind.SelectionRequested,
                SelectionPayload(1, 5)
            )
        );
        Assert.Equal("selection failed", fixture.Session.Failure!.Message);
    }

    private static byte[] SelectionPayload(uint index, ulong itemId)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, index);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), itemId);
        return bytes;
    }

    private sealed class ListSelectionProbe : ProbeView
    {
        internal ulong Token;
        internal ListSelectionEvent? Received;
        internal bool ThrowOnSelection;

        protected override Element Render(ref RenderContext ui)
        {
            Token = Runtime.Events.BindListSelection<ListSelectionProbe>(
                static (view, value) =>
                {
                    if (view.ThrowOnSelection)
                        throw new InvalidOperationException("selection failed");
                    view.Received = value;
                }
            );
            return base.Render(ref ui);
        }
    }
}
