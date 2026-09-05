using System.Runtime.CompilerServices;
using System.Text;
using Gpui.Interop;
using Gpui.Interop.Internal.Session;
using static Gpui.Units;

namespace Gpui.Tests;

public sealed unsafe class RenderOutputTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 2, 2)]
    [InlineData(256, 257, 512)]
    [InlineData(256, 5000, 5000)]
    [InlineData(1073741824, 1073741825, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    public void CapacityGrowthIsCheckedAndMakesProgress(int current, int required, int expected)
    {
        Assert.Equal(expected, ArenaCapacity.GrowTo(current, required));
    }

    [Fact]
    public void TinyBuffersGrowWithoutChangingExistingElementIdentity()
    {
        using var storage = new RenderArenaOwner(1, 1, 1, 1);
        var descriptor = (nuint)storage.NativeArena;
        var ui = storage.BeginRender();
        Element root = ui.Div();
        var generation = root.Generation;
        for (var index = 0; index < 400; index++)
        {
            var text = ui.Text("preserved UTF-8: 日本語"u8).Width(Px(10));
            ArenaWriter.AddChild(root, text);
        }
        ArenaWriter.AddF32(root, OpCode.WidthPx, 20);
        storage.Validate(root);
        Assert.Equal(descriptor, (nuint)storage.NativeArena);
        Assert.Equal(generation, root.Generation);
        Assert.Equal(401, storage.GetStats().Nodes);
        Assert.Equal(400, storage.GetStats().Children);
        Assert.Equal(401, storage.GetStats().Ops);
    }

    [Fact]
    public void PublicationBorrowsBuffersWithoutCopyingOrTransferringOwnership()
    {
        using var storage = new RenderArenaOwner(1, 1, 1, 1);
        var ui = storage.BeginRender();
        Element root = ui.Text("published"u8);
        RenderArena output = default;
        storage.PublishTo(&output, root);
        ManagedValidator.Validate(&output, new Element(&output, root.Node, output.Generation));
        Assert.Equal((nuint)storage.NativeArena->Nodes, (nuint)output.Nodes);
        Assert.Equal((nuint)storage.NativeArena->Utf8, (nuint)output.Utf8);
        Assert.Equal(0u, output.Flags);
        Assert.Equal(0, output.RequiredNodeCapacity);
        // output is only a borrowed descriptor: do not dispose or retain its pointers.
    }

    [Fact]
    public void LargeRootIsRenderedOnceAndReusesHighWaterCapacities()
    {
        var view = new LargeView();
        var session = CreateSession(view);
        try
        {
            RenderArena first = default;
            var root = session.RenderRootOutput(&first);
            ManagedValidator.Validate(&first, new Element(&first, root, first.Generation));
            Assert.Equal(1, view.RootCalls);
            Assert.True(first.NodeCapacity > 256);
            Assert.True(first.OpCapacity > 2048);
            Assert.True(first.ChildCapacity > 512);
            Assert.True(first.Utf8Capacity > 16 * 1024);
            var firstNodes = (nuint)first.Nodes;
            var firstBytes = (nuint)first.Utf8;
            var nodeCapacity = first.NodeCapacity;
            var utf8Capacity = first.Utf8Capacity;

            // A subsequent request deliberately renders again; growth within one request does not.
            RenderArena second = default;
            session.RenderRootOutput(&second);
            Assert.Equal(2, view.RootCalls);
            Assert.Equal(firstNodes, (nuint)second.Nodes);
            Assert.Equal(firstBytes, (nuint)second.Utf8);
            Assert.Equal(nodeCapacity, second.NodeCapacity);
            Assert.Equal(utf8Capacity, second.Utf8Capacity);
        }
        finally
        {
            session.Stop();
        }
    }

    [Fact]
    public void DemandRangeCallsEachRequestedRowOnceAndDoesNotOverwriteRootStorage()
    {
        var view = new LargeView();
        var session = CreateSession(view);
        try
        {
            RenderArena rootOutput = default;
            session.RenderRootOutput(&rootOutput);
            var originalTextByte = rootOutput.Utf8[0];
            var token = ((ulong)view.RuntimeViewHandle << 32) | 1;
            RenderArena rangeOutput = default;
            var root = session.RenderListRangeOutput(token, 10, 8, &rangeOutput);
            ManagedValidator.Validate(&rangeOutput, new Element(&rangeOutput, root, rangeOutput.Generation));
            Assert.Equal(8, view.RowCalls);
            Assert.Equal(8, rangeOutput.ChildLength);
            Assert.True(rangeOutput.Utf8Capacity > 16 * 1024);
            Assert.NotEqual((nuint)rootOutput.Utf8, (nuint)rangeOutput.Utf8);
            Assert.Equal(originalTextByte, rootOutput.Utf8[0]);
        }
        finally
        {
            session.Stop();
        }
    }

    [Fact]
    public void FailedUserRenderDoesNotPublishPartialOutputOrRetry()
    {
        var view = new ThrowingView();
        var session = CreateSession(view);
        try
        {
            RenderArena output = default;
            var threw = false;
            try
            {
                session.RenderRootOutput(&output);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Assert.True(threw);
            Assert.Equal(1, view.Calls);
            Assert.Equal((nuint)0, (nuint)output.Nodes);
            Assert.Equal(0, output.NodeLength);
        }
        finally
        {
            session.Stop();
        }
    }

    [Fact]
    public void InterpolatedTextSurvivesMultipleGrowthOperations()
    {
        using var storage = new RenderArenaOwner(1, 1, 1, 1);
        var ui = storage.BeginRender();
        var longText = new string('x', 8192);
        Element root = ui.Text($"prefix:{longText}:{42:D1200}:{true}:suffix");
        storage.Validate(root);
        var node = storage.NativeArena->Nodes[root.Node];
        var text = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(
            storage.NativeArena->Utf8 + node.DataOffset, checked((int)node.DataLength)));
        Assert.Equal($"prefix:{longText}:{42:D1200}:{true}:suffix", text);
    }

    [Fact]
    public void InterleavedUtf8WritesAreRejectedBeforeAHandlerReusesAStaleSpan()
    {
        using var storage = new RenderArenaOwner(1, 1, 1, 1);
        var ui = storage.BeginRender();
        var handler = new Utf8InterpolatedStringHandler(1, 1, ui);
        handler.AppendLiteral("a");
        _ = ui.Text(new string('x', 4096));
        var threw = false;
        try
        {
            handler.AppendLiteral("b");
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyKeyedSlotsAllowAcceptedTypeReplacement(bool keyed)
    {
        var view = new SwitchingView { Keyed = keyed };
        var session = CreateSession(view);
        try
        {
            RenderArena output = default;
            session.RenderRootOutput(&output);
            view.Second = true;
            var threw = false;
            try
            {
                session.RenderRootOutput(&output);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Assert.Equal(!keyed, threw);
        }
        finally
        {
            session.Stop();
        }
    }

    private static ManagedSession CreateSession(View view)
    {
        // These fixtures never notify or dispatch a native command. An inert runtime
        // avoids loading a platform library while exercising the actual session renderer.
        var runtime = (NativeRuntime)RuntimeHelpers.GetUninitializedObject(typeof(NativeRuntime));
        return new ManagedSession(runtime, new GpuiApplication(), 1, view);
    }

    private sealed class LargeView : View
    {
        private readonly string _text = new('x', 128);
        private readonly string _rowText = new('r', 16384);
        internal int RootCalls;
        internal int RowCalls;

        protected override Element Render(ref RenderContext ui)
        {
            RootCalls++; // Test instrumentation, not application render behavior.
            Element root = ui.Div();
            for (var index = 0; index < 1024; index++)
            {
                var text = ui.Text(_text).Width(Px(10)).Height(Px(10)).MinWidth(Px(5));
                ArenaWriter.AddChild(root, text);
            }
            return root;
        }

        protected override Element RenderListItem(uint rendererId, int index, ref RenderContext ui)
        {
            RowCalls++;
            return ui.Text(_rowText);
        }
    }

    private sealed class ThrowingView : View
    {
        internal int Calls;
        protected override Element Render(ref RenderContext ui)
        {
            Calls++;
            _ = ui.Text("partial output"u8);
            throw new InvalidOperationException("Deliberate test fault.");
        }
    }

    private sealed class SwitchingView : View
    {
        internal bool Keyed;
        internal bool Second;
        protected override Element Render(ref RenderContext ui) => Second
            ? (Keyed ? ui.Child<SecondChild>("slot") : ui.Child<SecondChild>())
            : (Keyed ? ui.Child<FirstChild>("slot") : ui.Child<FirstChild>());
    }

    private sealed class FirstChild : View, IGeneratedViewFactory<FirstChild>
    {
        public static FirstChild CreateGpuiView() => new();
        protected override Element Render(ref RenderContext ui) => ui.Div();
    }

    private sealed class SecondChild : View, IGeneratedViewFactory<SecondChild>
    {
        public static SecondChild CreateGpuiView() => new();
        protected override Element Render(ref RenderContext ui) => ui.Div();
    }
}
