using Gpui;

namespace Gpui.Tests;

public sealed class MenuImmutabilityRegressionTests
{
    [Fact]
    public void MenuItemsCannotBeMutatedThroughTheExposedCollection()
    {
        var item = GpuiMenuItem.Command("Original", static () => { });
        var menu = new GpuiMenu("File", item);
        var exposed = Assert.IsAssignableFrom<IList<GpuiMenuItem>>(menu.Items);

        Assert.True(exposed.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => exposed[0] = GpuiMenuItem.Submenu(menu));
        Assert.Same(item, menu.Items[0]);
    }

    [Fact]
    public void MenuStillCopiesTheCallerArray()
    {
        var original = GpuiMenuItem.Command("Original", static () => { });
        var supplied = new[] { original };
        var menu = new GpuiMenu("File", supplied);
        supplied[0] = GpuiMenuItem.Separator();

        Assert.Same(original, menu.Items[0]);
    }
}
