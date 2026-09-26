using System.Buffers.Binary;
using Gpui.Components;

namespace Gpui.Tests;

public sealed class ComponentExtensionTests
{
    [Fact]
    public void ComponentSchemaIdentityIsIndependentFromEditor()
    {
        Assert.Equal("gpui.net.components", ComponentsExtension.Requirement.Id);
        Assert.Equal(12u, ComponentsExtension.Requirement.Version);
        Assert.Equal(ComponentSchema.SchemaHash, ComponentsExtension.SchemaHash);
        Assert.NotEqual(Gpui.Editor.EditorExtension.SchemaHash, ComponentsExtension.SchemaHash);
    }

    [Fact]
    public void GeneratedConfigurationEncoderUsesStableInvariantFields()
    {
        Assert.Equal(
            "medium\nprimary\nSave\nSave document\n0\n1\n0\n1\n0\nicons/save.svg\n42",
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
                "icons/save.svg",
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
                string.Empty,
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
            "small\nvertical\nuploading\nreport.pdf\n\"Uploading\"\n\n1\n1\n1\n17",
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
            "icon\nNo files\n\"Add a file to begin.\"\n1\n0\n1",
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
    public void MultilineConfigurationTextStaysOneSchemaField()
    {
        Assert.Equal(
            "icon\nNo files\n\"First line\\nSecond line\"\n0\n0\n0",
            ComponentSchema.Empty.EncodeConfiguration(
                ComponentSchema.Empty.MediaVariant.Icon,
                "No files",
                "First line\nSecond line",
                false,
                false,
                false
            )
        );
        Assert.Throws<ArgumentException>(() =>
            ComponentSchema.Empty.EncodeConfiguration(
                ComponentSchema.Empty.MediaVariant.Icon,
                "No files",
                "Invalid\0text",
                false,
                false,
                false
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

    [Fact]
    public void IconAndAvatarConfigurationsCarryNativeMediaSources()
    {
        Assert.Equal(
            "large\nicons/archive.svg\n#ffffff",
            ComponentSchema.Icon.EncodeConfiguration(
                ComponentSchema.Icon.Size.Large,
                "icons/archive.svg",
                "#ffffff"
            )
        );
        Assert.Equal(
            "small\nAlex\nhttps://example.com/alex.png",
            ComponentSchema.Avatar.EncodeConfiguration(
                ComponentSchema.Avatar.Size.Small,
                "Alex",
                "https://example.com/alex.png"
            )
        );
    }

    [Fact]
    public void DescriptionListConfigurationEncodesAStableSpanBatch()
    {
        Assert.Equal(
            "small\nhorizontal\n120\n1\n2\n1,1,0,2",
            ComponentSchema.DescriptionList.EncodeConfiguration(
                ComponentSchema.DescriptionList.Size.Small,
                ComponentSchema.DescriptionList.Axis.Horizontal,
                120,
                true,
                2,
                [1, 1, 0, 2]
            )
        );
        Assert.EndsWith(
            "\n",
            ComponentSchema.DescriptionList.EncodeConfiguration(
                ComponentSchema.DescriptionList.Size.Medium,
                ComponentSchema.DescriptionList.Axis.Vertical,
                80,
                false,
                1,
                []
            )
        );
    }

    [Fact]
    public void BreadcrumbConfigurationBatchesUnicodeAndStableIds()
    {
        Assert.Equal(
            "[\"Files\",\"R\\u00E9sum\\u00E9\\n2026\"]\n1,7\n0,1\n42",
            ComponentSchema.Breadcrumb.EncodeConfiguration(
                ["Files", "Résumé\n2026"],
                [1, 7],
                [0, 1],
                42
            )
        );
        var payload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 7);
        Assert.Equal(
            7u,
            ComponentBreadcrumbClickedEvent
                .Decode(
                    new NativeExtensionEvent(ComponentSchema.Breadcrumb.EventClicked, 0, 0, payload)
                )
                .ItemId
        );
        Assert.Throws<InvalidOperationException>(() =>
            ComponentBreadcrumbClickedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Breadcrumb.EventClicked, 0, 0, [])
            )
        );
    }

    [Fact]
    public void TabsConfigurationBatchesControlledSelectionAndStableIds()
    {
        Assert.Equal(
            "medium\nunderline\n[\"Overview\",\"R\\u00E9sum\\u00E9\"]\n1,7\n0,1\n1\n1\n1\n42",
            ComponentSchema.Tabs.EncodeConfiguration(
                ComponentSchema.Tabs.Size.Medium,
                ComponentSchema.Tabs.Variant.Underline,
                ["Overview", "Résumé"],
                [1, 7],
                [0, 1],
                1,
                true,
                true,
                42
            )
        );
        var payload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 7);
        Assert.Equal(
            7u,
            ComponentTabSelectedEvent
                .Decode(new NativeExtensionEvent(ComponentSchema.Tabs.EventSelected, 0, 0, payload))
                .ItemId
        );
        Assert.Throws<InvalidOperationException>(() =>
            ComponentTabSelectedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Tabs.EventSelected, 0, 0, [])
            )
        );
    }
}
