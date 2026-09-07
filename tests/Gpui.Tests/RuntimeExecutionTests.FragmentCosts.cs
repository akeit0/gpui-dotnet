using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(128)]
    [InlineData(4096)]
    [InlineData(16384)]
    public void RetainedFragmentCopyAndValidationCost(int leaves)
    {
        using var source = new RenderArenaOwner();
        using var destination = new RenderArenaOwner();
        var ui = source.BeginRender();
        var root = ui.Div();
        for (var index = 0; index < leaves; index++)
            root = root.Children(ui.Text("retained row label")
                .FontSize(Units.Px(14)).TextColor(new Color(0x112233FF)));
        source.Validate(root);
        var sourceRoot = root.Inner.Node;
        Element copied = default;
        MeasureRenderCost($"fragment-reset-copy-{leaves}", 64, () =>
        {
            destination.BeginRender();
            copied = ArenaWriter.AppendFragment(destination, source, sourceRoot);
        });
        destination.Validate(copied);
        Assert.Equal(source.GetStats().Nodes, destination.GetStats().Nodes);
        Assert.Equal(source.GetStats().Ops, destination.GetStats().Ops);
        MeasureRenderCost($"fragment-validate-{leaves}", 64, () => destination.Validate(copied));
        var input = source.NativeArena;
        var output = destination.NativeArena;
        var copiedBytes = input->NodeLength * sizeof(NodeRecord)
            + input->OpLength * sizeof(OpRecord)
            + input->ChildLength * sizeof(ChildRecord) + input->Utf8Length;
        var capacityBytes = output->NodeCapacity * sizeof(NodeRecord)
            + output->OpCapacity * sizeof(OpRecord)
            + output->ChildCapacity * sizeof(ChildRecord) + output->Utf8Capacity;
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"fragment-{leaves}: copied={copiedBytes} bytes; destination-capacity={capacityBytes} bytes");
    }
}
