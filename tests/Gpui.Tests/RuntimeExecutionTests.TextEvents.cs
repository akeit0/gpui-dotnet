using System.Text;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData((int)InputEventKind.Changed)]
    [InlineData((int)InputEventKind.Submitted)]
    [InlineData((int)InputEventKind.FocusChanged)]
    [InlineData((int)DockEventKind.LayoutExported)]
    [InlineData((int)DockEventKind.PanelClosed)]
    [InlineData((int)KeyEventKind.Down)]
    [InlineData((int)KeyEventKind.Up)]
    [InlineData((int)FileEventKind.Dropped)]
    public void NativeTextEventsRejectMalformedUtf8BeforeDelivery(int kind)
    {
        var view = new TextEventProbe();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        byte[][] malformed =
        [
            [0x80], // Isolated continuation.
            [0xC0, 0xAF], // Overlong encoding.
            [0xE2, 0x82], // Truncated sequence.
            [0xED, 0xA0, 0x80], // Surrogate.
            [0xF4, 0x90, 0x80, 0x80], // Beyond Unicode.
        ];
        foreach (var bytes in malformed)
        {
            Assert.Equal(
                -112,
                fixture.Control(view.Token(kind), (ushort)kind, TextEventPayload(kind, bytes))
            );
            Assert.Null(view.Received);
            Assert.Null(fixture.Session.Failure);
        }

        // A literal replacement character is valid Unicode, as are combining and supplementary characters.
        const string text = "日本語 e\u0301 😀 \uFFFD";
        var payload = TextEventPayload(kind, Encoding.UTF8.GetBytes(text));
        Assert.Equal(0, fixture.Control(view.Token(kind), (ushort)kind, payload));
        Array.Fill(payload, (byte)0xFF);
        switch (view.Received)
        {
            case InputEvent input:
                Assert.Equal(Encoding.UTF8.GetBytes(text), input.Utf8Value.ToArray());
                Assert.Equal(text, input.Value);
                break;
            case DockEvent dock:
                Assert.Equal(
                    text,
                    kind == (int)DockEventKind.LayoutExported ? dock.LayoutJson : dock.PanelId
                );
                break;
            case KeyEvent key:
                Assert.Equal(text, key.Key);
                break;
            case FileDropEvent drop:
                Assert.Equal(["first.txt", text], drop.Paths);
                break;
            default:
                Assert.Fail("Expected the matching text event handler to run.");
                break;
        }
    }

    [Fact]
    public void EmptyInputAndDockNotificationRemainValidAndExtensionPayloadStaysBinary()
    {
        var view = new TextEventProbe();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        Assert.Equal(
            0,
            fixture.Control(
                view.Token((int)InputEventKind.Changed),
                (ushort)InputEventKind.Changed,
                []
            )
        );
        Assert.Equal(string.Empty, Assert.IsType<InputEvent>(view.Received).Value);
        Assert.Equal(
            0,
            fixture.Control(
                view.Token((int)DockEventKind.LayoutChanged),
                (ushort)DockEventKind.LayoutChanged,
                []
            )
        );
        Assert.IsType<DockEvent>(view.Received);

        byte[] payload = [0xFF, 0x00, 0xC0];
        Assert.Equal(0, fixture.Control(view.ExtensionToken, 0x8001, payload));
        Array.Clear(payload);
        Assert.Equal(
            new byte[] { 0xFF, 0x00, 0xC0 },
            Assert.IsType<BinaryEvent>(view.Received).Event.Payload.ToArray()
        );
        Assert.Null(fixture.Session.Failure);
    }

    private static byte[] TextEventPayload(int kind, byte[] text) =>
        kind == (int)FileEventKind.Dropped
            ? [0, 0, 0, 0, 0, 0, 0, 0, .. "first.txt\0"u8, .. text]
            : text;

    private readonly record struct BinaryEvent(NativeExtensionEvent Event)
        : INativeExtensionEvent<BinaryEvent>
    {
        public static BinaryEvent Decode(NativeExtensionEvent nativeEvent) => new(nativeEvent);
    }

    private sealed class TextEventProbe : ProbeView
    {
        internal object? Received;
        internal ulong ExtensionToken;
        private ulong _inputToken;
        private ulong _dockToken;
        private ulong _keyToken;
        private ulong _fileDropToken;

        internal ulong Token(int kind) =>
            kind switch
            {
                (int)InputEventKind.Changed
                or (int)InputEventKind.Submitted
                or (int)InputEventKind.FocusChanged => _inputToken,
                (int)DockEventKind.LayoutChanged
                or (int)DockEventKind.LayoutExported
                or (int)DockEventKind.PanelClosed => _dockToken,
                (int)KeyEventKind.Down or (int)KeyEventKind.Up => _keyToken,
                (int)FileEventKind.Dropped => _fileDropToken,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

        protected override Element Render(ref RenderContext ui)
        {
            _inputToken = Runtime.Events.BindInput<TextEventProbe>(
                static (view, value) => view.Received = value
            );
            _dockToken = Runtime.Events.BindDock<TextEventProbe>(
                static (view, value) => view.Received = value
            );
            _keyToken = Runtime.Events.BindKey<TextEventProbe>(
                static (view, value) => view.Received = value
            );
            _fileDropToken = Runtime.Events.BindFileDrop<TextEventProbe>(
                static (view, value) => view.Received = value
            );
            ExtensionToken = Runtime.Events.BindNativeExtensionEvent<TextEventProbe, BinaryEvent>(
                static (view, value) => view.Received = value
            );
            return base.Render(ref ui);
        }
    }
}
