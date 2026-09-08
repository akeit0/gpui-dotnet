using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConditionalInputReplacementPreservesPayloadAcrossAnyThreadRoute(
        bool utf8,
        bool explicitPolicies
    )
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var calls = fixture.CaptureResourceCommands();
        var controller = new InputController(fixture.View, "入力");
        const ulong revision = ulong.MaxValue - 3;
        var selection = explicitPolicies
            ? InputSelectionPolicy.MoveToEnd
            : InputSelectionPolicy.Preserve;
        var composition = explicitPolicies
            ? InputCompositionPolicy.CancelComposition
            : InputCompositionPolicy.RejectWhileComposing;
        void Replace()
        {
            if (utf8)
                controller.SetValueIfCurrent("変換🙂"u8, revision, selection, composition);
            else
                controller.SetValueIfCurrent("変換🙂", revision, selection, composition);
        }

        Exception? failure = null;
        var worker = new Thread(() => failure = Record.Exception(Replace));
        worker.Start();
        worker.Join();
        Assert.Null(failure);
        var (owner, command) = Assert.Single(calls);
        Assert.Equal(fixture.View.Runtime.RuntimeViewHandle, owner);
        Assert.Equal(
            new ResourceCommand(
                ResourceKind.Input,
                ResourceCommandKind.InputSetValueIfCurrent,
                "入力",
                revision,
                explicitPolicies ? 3UL : 0UL,
                "変換🙂"
            ),
            command
        );

        fixture.Session.Stop();
        Assert.Throws<InvalidOperationException>(Replace);
        Assert.Single(calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalInputReplacementRejectsMalformedArgumentsBeforeDispatch(bool utf8)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var calls = fixture.CaptureResourceCommands();
        var controller = new InputController(fixture.View, "input");
        void Replace(
            ulong revision,
            InputSelectionPolicy selection,
            InputCompositionPolicy composition
        )
        {
            if (utf8)
                controller.SetValueIfCurrent(""u8, revision, selection, composition);
            else
                controller.SetValueIfCurrent("", revision, selection, composition);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => Replace(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Replace(1, (InputSelectionPolicy)2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Replace(1, 0, (InputCompositionPolicy)2));
        Assert.Throws<ArgumentNullException>(() => controller.SetValueIfCurrent((string)null!, 1));
        Assert.Empty(calls);
    }
}
