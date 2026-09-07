namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void InputGalleryRendersFieldsWithAndWithoutHelpText()
    {
        var application = new GpuiApplication();
        var spec = InputGalleryView.Spec();
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(null, application,
            new RootViewDeclaration<InputGalleryView>(spec), window);

        Assert.Equal(0, fixture.NativePublish(out var revision));
        Assert.Null(fixture.Session.Failure);
        Assert.Equal(0, fixture.Complete(revision));

        application.SetTheme(GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark));
        fixture.RenderFromNative();
        Assert.Null(fixture.Session.Failure);
    }
}
