using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourceFailureReportsOperationAndIdentityWithoutValuePayload(bool utf8)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        fixture.ResourceStatus = -34;
        fixture.View.OnClick = () =>
        {
            var owner = fixture.View.Runtime.RuntimeViewHandle;
            if (utf8)
                fixture.Session.DispatchUtf8InputValue(owner, "入力"u8, "private document text"u8);
            else
                fixture.Session.DispatchResourceCommand(
                    owner,
                    new ResourceCommand(
                        ResourceKind.Input,
                        ResourceCommandKind.InputSetValue,
                        "入力",
                        0,
                        0,
                        Data: "private document text"
                    )
                );
        };
        Assert.Equal(-111, fixture.Click());
        var message = fixture.Session.Failure!.Message;
        Assert.Contains("InputSetValue", message);
        Assert.Contains("owner 1", message);
        Assert.Contains("入力", message);
        Assert.Contains("ResourceCommand.ResourceNotDeclared", message);
        Assert.Contains("status -34", message);
        Assert.DoesNotContain("private document text", message);
    }
}
