using System.Buffers.Binary;
using Gpui.Components;

namespace Gpui.Tests;

public sealed class ComponentExtensionTests
{
    [Fact]
    public void ComponentSchemaIdentityIsIndependentFromEditor()
    {
        Assert.Equal("gpui.net.components", ComponentsExtension.Requirement.Id);
        Assert.Equal(7u, ComponentsExtension.Requirement.Version);
        Assert.Equal(ComponentSchema.SchemaHash, ComponentsExtension.SchemaHash);
        Assert.NotEqual(Gpui.Editor.EditorExtension.SchemaHash, ComponentsExtension.SchemaHash);
    }

    [Fact]
    public void GeneratedConfigurationEncoderUsesStableInvariantFields()
    {
        Assert.Equal(
            "medium\nprimary\nSave\nSave document\n0\n1\n0\n1\n0\n42",
            ComponentSchema.Button.EncodeConfiguration(
                ComponentSchema.Button.Size.Medium,
                ComponentSchema.Button.Variant.Primary,
                "Save",
                "Save document",
                false,
                true,
                false,
                true,
                false,
                42
            )
        );
        Assert.Equal(
            "large\n80\n68\n0\n\nUpload progress",
            ComponentSchema.ProgressCircle.EncodeConfiguration(
                ComponentSchema.ProgressCircle.Size.Large,
                80,
                68,
                false,
                string.Empty,
                "Upload progress"
            )
        );
        Assert.Throws<ArgumentException>(() =>
            ComponentSchema.Button.EncodeConfiguration(
                ComponentSchema.Button.Size.Medium,
                ComponentSchema.Button.Variant.Primary,
                "Save\nnow",
                string.Empty,
                false,
                false,
                false,
                false,
                false,
                0
            )
        );
    }

    [Fact]
    public void ComponentEventsValidateTheirSchemaOwnedPayloads()
    {
        var value = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(value, 4);
        var changed = ComponentRatingChangedEvent.Decode(
            new NativeExtensionEvent(ComponentSchema.Rating.EventChanged, 0, 0, value)
        );
        Assert.Equal(4u, changed.Value);

        _ = ComponentClickedEvent.Decode(
            new NativeExtensionEvent(ComponentSchema.Button.EventClicked, 0, 0, [])
        );
        Assert.Throws<InvalidOperationException>(() =>
            ComponentClickedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Button.EventClicked, 1, 0, [])
            )
        );
    }

    [Fact]
    public void GeneratedConfigurationEncoderEscapesCSharpKeywordFields()
    {
        Assert.Equal(
            "small\n1\n0\nWi-Fi\nWireless network\nNetwork state\n#336699\n17",
            ComponentSchema.Switch.EncodeConfiguration(
                ComponentSchema.Switch.Size.Small,
                true,
                false,
                "Wi-Fi",
                "Wireless network",
                "Network state",
                "#336699",
                17
            )
        );
    }

    [Fact]
    public void AdditionalControlledEventsValidatePayloads()
    {
        var enabled = ComponentSwitchChangedEvent.Decode(
            new NativeExtensionEvent(ComponentSchema.Switch.EventChanged, 0, 0, [1])
        );
        Assert.True(enabled.Value);
        Assert.Throws<InvalidOperationException>(() =>
            ComponentSwitchChangedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Switch.EventChanged, 0, 0, [2])
            )
        );

        var page = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(page, 7);
        Assert.Equal(
            7u,
            ComponentPageChangedEvent
                .Decode(
                    new NativeExtensionEvent(ComponentSchema.Pagination.EventChanged, 0, 0, page)
                )
                .Page
        );
    }

    [Fact]
    public void AttachmentConfigurationAndClickEventUseTheGeneratedContract()
    {
        Assert.Equal(
            "small\nvertical\nuploading\nreport.pdf\nUploading\n\n1\n1\n1\n17",
            ComponentSchema.Attachment.EncodeConfiguration(
                ComponentSchema.Attachment.Size.Small,
                ComponentSchema.Attachment.Axis.Vertical,
                ComponentSchema.Attachment.Status.Uploading,
                "report.pdf",
                "Uploading",
                string.Empty,
                true,
                true,
                true,
                17
            )
        );
        _ = ComponentAttachmentClickedEvent.Decode(
            new NativeExtensionEvent(ComponentSchema.Attachment.EventClicked, 0, 0, [])
        );
        Assert.Throws<InvalidOperationException>(() =>
            ComponentAttachmentClickedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Attachment.EventClicked, 0, 0, [1])
            )
        );
    }

    [Fact]
    public void EmptyConfigurationKeepsTheNamedSlotPresence()
    {
        Assert.Equal(
            "icon\nNo files\nAdd a file to begin.\n1\n0\n1",
            ComponentSchema.Empty.EncodeConfiguration(
                ComponentSchema.Empty.MediaVariant.Icon,
                "No files",
                "Add a file to begin.",
                true,
                false,
                true
            )
        );
    }

    [Fact]
    public void ToolbarConfigurationsCarryDensityAndGroupName()
    {
        Assert.Equal(
            "small\n1",
            ComponentSchema.Toolbar.EncodeConfiguration(ComponentSchema.Toolbar.Size.Small, true)
        );
        Assert.Equal(
            "Document actions",
            ComponentSchema.ToolbarGroup.EncodeConfiguration("Document actions")
        );
    }

    [Fact]
    public void StatusBarConfigurationKeepsAllThreeRegions()
    {
        Assert.Equal("1\n0\n1", ComponentSchema.StatusBar.EncodeConfiguration(true, false, true));
    }

    [Fact]
    public void ConversationConfigurationsSupportNamedSlotsAndEmptyGroups()
    {
        Assert.Equal(string.Empty, ComponentSchema.BubbleGroup.EncodeConfiguration());
        Assert.Equal(string.Empty, ComponentSchema.MessageGroup.EncodeConfiguration());
        Assert.Equal(
            "ghost\nend\ntop\nstart\n1\n1",
            ComponentSchema.Bubble.EncodeConfiguration(
                ComponentSchema.Bubble.Variant.Ghost,
                ComponentSchema.Bubble.Alignment.End,
                ComponentSchema.Bubble.ReactionSide.Top,
                ComponentSchema.Bubble.ReactionAlignment.Start,
                true,
                true
            )
        );
        Assert.Equal(
            "end\n1\n0\n1\n1\n1\n0",
            ComponentSchema.Message.EncodeConfiguration(
                ComponentSchema.Message.Alignment.End,
                true,
                false,
                true,
                true,
                true,
                false
            )
        );
        Assert.Equal(
            "separator\ncenter\n1\nshimmer\n1\nLoading\n0\n0",
            ComponentSchema.Marker.EncodeConfiguration(
                ComponentSchema.Marker.Variant.Separator,
                ComponentSchema.Marker.Alignment.Center,
                true,
                ComponentSchema.Marker.LoadingStyle.Shimmer,
                true,
                "Loading",
                false,
                false
            )
        );
    }
}
