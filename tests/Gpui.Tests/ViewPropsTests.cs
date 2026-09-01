using Gpui;

namespace Gpui.Tests;

public sealed class ViewPropsTests
{
    [Fact]
    public void PropsRemainConstructorLikeAcrossCommitAndRollback()
    {
        var view = new ProbeView();

        Assert.Throws<InvalidOperationException>(() => view.ReadProps());

        Assert.True(view.StageProps(1));
        Assert.Equal(1, view.ReadProps());
        view.ValidateRenderInputs();
        view.CommitStagedProps();
        Assert.Equal(1, view.ReadProps());

        Assert.True(view.StageProps(2));
        Assert.Equal(2, view.ReadProps());
        view.ValidateRenderInputs();
        view.RollBackStagedProps();
        Assert.Equal(1, view.ReadProps());

        Assert.False(view.StageProps(2));
        view.RollBackStagedProps();
        Assert.True(view.StageProps(1));
    }

    private sealed class ProbeView : View<int>
    {
        internal int ReadProps() => Props;

        protected override Element Render(ref RenderContext ui) => ui.Text($"{Props}");
    }
}
