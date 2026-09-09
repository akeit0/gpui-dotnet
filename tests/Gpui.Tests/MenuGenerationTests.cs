using Gpui;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed class MenuGenerationTests
{
    [Fact]
    public void ReplacementRetainsOldActionsUntilNativeAcknowledgesAndRejectionPreservesThem()
    {
        var generations = new MenuGenerations();
        var calls = 0;
        var first = generations.Stage(new() { [1] = () => calls++ });
        generations.Applied(first);
        var rejected = generations.Stage(new() { [2] = () => calls += 10 });
        generations.Reject(rejected);
        generations.Find(1)!();
        Assert.Null(generations.Find(2));
        var replacement = generations.Stage(new() { [3] = () => calls += 100 });
        generations.Find(1)!();
        generations.Applied(replacement);
        Assert.Null(generations.Find(1));
        generations.Find(3)!();
        Assert.Equal(102, calls);
        var empty = generations.Stage([]);
        generations.Applied(empty);
        Assert.Null(generations.Find(3));
        generations.Clear();
    }

    [Fact]
    public void PendingGenerationsAreBoundedAndAcknowledgementReleasesCapacity()
    {
        var generations = new MenuGenerations();
        ulong latest = 0;
        for (ulong id = 1; id <= 64; id++)
            latest = generations.Stage(new() { [id] = static () => { } });
        Assert.Throws<InvalidOperationException>(() => generations.Stage([]));
        generations.Applied(latest);
        generations.Stage([]);
        generations.Clear();
        Assert.Null(generations.Find(64));
    }

    [Fact]
    public void RejectedMenuLimitsLeaveAcceptedSnapshotIntact()
    {
        var application = new GpuiApplication();
        var original = new GpuiMenu("File", GpuiMenuItem.Command("Open", static () => { }));
        application.SetMenuBar(original);
        var items = Enumerable.Repeat(GpuiMenuItem.Separator(), 4096).ToArray();
        Assert.Throws<ArgumentException>(() =>
            application.SetMenuBar(new GpuiMenu("Large", items))
        );
        var nested = new GpuiMenu("Leaf");
        for (var depth = 1; depth < 32; depth++)
            nested = new GpuiMenu("Nested", GpuiMenuItem.Submenu(nested));
        GpuiMenu.Validate(nested, "menu");
        Assert.Throws<ArgumentException>(() =>
            application.SetMenuBar(new GpuiMenu("Too deep", GpuiMenuItem.Submenu(nested)))
        );
        Assert.Throws<ArgumentException>(() =>
            application.SetMenuBar(new GpuiMenu(new string('é', 524289)))
        );
        Assert.Same(original, Assert.Single(application.MenuBarSnapshot()!));
    }
}
