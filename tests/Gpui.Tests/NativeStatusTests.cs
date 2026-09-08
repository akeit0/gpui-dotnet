using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed class NativeStatusTests
{
    [Fact]
    public void OverlappingStatusesKeepTheirOperationMeaning()
    {
        Assert.Contains(
            "SessionMissing",
            NativeStatus.Describe(NativeStatusDomain.ResourceCommand, -30)
        );
        Assert.Contains(
            "InvalidImageObjectFit",
            NativeStatus.Describe(NativeStatusDomain.Snapshot, -30)
        );
        Assert.Contains(
            "InvalidThemePayload",
            NativeStatus.Describe(NativeStatusDomain.ApplicationCommand, -64)
        );
        Assert.Contains(
            "InvalidMenuPointer",
            NativeStatus.Describe(NativeStatusDomain.ApplicationMenu, -64)
        );
    }

    [Theory]
    [InlineData(-12345)]
    [InlineData(1)]
    public void UnrecognizedStatusesRemainVisible(int status)
    {
        var message = NativeStatus.Describe(NativeStatusDomain.Notification, status);
        Assert.Contains("UnknownStatus", message);
        Assert.Contains($"status {status}", message);
    }

    [Fact]
    public void AmbiguousSnapshotStatusesDoNotInventAPreciseCause()
    {
        Assert.Contains(
            "EmptyOperationDataOrWrongRowCount",
            NativeStatus.Describe(NativeStatusDomain.Snapshot, -63)
        );
        Assert.Contains(
            "InvalidFontDataOrMissingArtifact",
            NativeStatus.Describe(NativeStatusDomain.Snapshot, -64)
        );
    }
}
