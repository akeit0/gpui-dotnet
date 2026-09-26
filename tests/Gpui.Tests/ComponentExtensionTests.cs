using System.Buffers.Binary;
using Gpui.Components;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed class ComponentExtensionTests
{
    [Fact]
    public void ComponentSchemaIdentityIsIndependentFromEditor()
    {
        Assert.Equal("gpui.net.components", ComponentsExtension.Requirement.Id);
        Assert.Equal(18u, ComponentsExtension.Requirement.Version);
        Assert.Equal(ComponentSchema.SchemaHash, ComponentsExtension.SchemaHash);
        Assert.NotEqual(Gpui.Editor.EditorExtension.SchemaHash, ComponentsExtension.SchemaHash);
    }

    [Fact]
    public void SelectionBatchesRequireStableDistinctIdsAndValidSelections()
    {
        var batch = SelectionBatch.Create(
            [new(10, "Ready"), new(20, "Blocked", Disabled: true)],
            [20u]
        );
        Assert.Equal([10u, 20u], batch.Ids);
        Assert.Equal([0u, 1u], batch.Disabled);
        Assert.Equal([20u], batch.Selected);
        Assert.Throws<ArgumentException>(() =>
            SelectionBatch.Create([new(10, "Ready"), new(10, "Again")], [])
        );
        Assert.Throws<ArgumentException>(() => SelectionBatch.Create([new(10, "Ready")], [20u]));
        Assert.Throws<ArgumentException>(() => SelectionBatch.Create([new(0, "Ready")], []));
    }

    [Fact]
    public void SelectionEventsValidateOptionalAndBatchedIds()
    {
        Assert.Null(
            ComponentSelectSelectedEvent
                .Decode(new NativeExtensionEvent(ComponentSchema.Select.EventSelected, 0, 0, []))
                .ItemId
        );
        Assert.Equal(
            10u,
            ComponentSelectSelectedEvent
                .Decode(
                    new NativeExtensionEvent(
                        ComponentSchema.Select.EventSelected,
                        0,
                        0,
                        [10, 0, 0, 0]
                    )
                )
                .ItemId
        );
        Assert.Throws<InvalidOperationException>(() =>
            ComponentSelectSelectedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Select.EventSelected, 0, 0, [0, 0, 0, 0])
            )
        );
        Assert.Equal(
            [10u, 20u],
            ComponentComboboxChangedEvent
                .Decode(
                    new NativeExtensionEvent(
                        ComponentSchema.Combobox.EventChanged,
                        0,
                        0,
                        [10, 0, 0, 0, 20, 0, 0, 0]
                    )
                )
                .ItemIds
        );
        Assert.Throws<InvalidOperationException>(() =>
            ComponentComboboxChangedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Combobox.EventChanged, 0, 0, [10])
            )
        );
    }

    [Fact]
    public void TreeBatchValidatesPreorderAndStableIds()
    {
        var batch = TreeBatch.Create(
            [new("src", "src", 0, InitiallyExpanded: true), new("src/main", "main", 1)],
            "src/main"
        );
        Assert.Equal(["src", "src/main"], batch.Ids);
        Assert.Equal([0u, 1u], batch.Depths);
        Assert.Equal([1u, 0u], batch.InitiallyExpanded);
        Assert.Throws<ArgumentException>(() =>
            TreeBatch.Create([new("src", "src", 0), new("missing", "missing", 2)], null)
        );
        Assert.Throws<ArgumentException>(() =>
            TreeBatch.Create([new("src", "src", 0), new("src", "again", 0)], null)
        );
        Assert.Throws<ArgumentException>(() => TreeBatch.Create([new("src", "src", 0)], "absent"));
        Assert.Throws<ArgumentException>(() =>
            TreeBatch.Create([new("\uD800", "invalid", 0)], null)
        );
        Assert.Throws<ArgumentException>(() =>
            TreeBatch.Create([new("src", "bad\nlabel", 0)], null)
        );
    }

    [Fact]
    public void TreeEventsValidateKindMetadataAndUtf8Id()
    {
        var valid = ComponentTreeEvent.Decode(
            new NativeExtensionEvent(
                ComponentSchema.Tree.EventExpanded,
                0,
                0,
                "src/main"u8.ToArray()
            )
        );
        Assert.Equal(ComponentTreeEventKind.Expanded, valid.Kind);
        Assert.Equal("src/main", valid.ItemId);
        Assert.Throws<InvalidOperationException>(() =>
            ComponentTreeEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Tree.EventCollapsed, 0, 0, [0xFF])
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            ComponentTreeEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Tree.EventSelectionRequested, 1, 0, [65])
            )
        );
    }

    [Fact]
    public void AccordionBatchesRequireStableIdsAndControlledOpenSet()
    {
        var batch = AccordionBatch.Create(
            [new(10, "First", default), new(20, "Second", default, Disabled: true)],
            [20u],
            false
        );
        Assert.Equal([10u, 20u], batch.Ids);
        Assert.Equal([0u, 1u], batch.Disabled);
        Assert.Equal([20u], batch.OpenIds);
        Assert.Throws<ArgumentException>(() =>
            AccordionBatch.Create([new(10, "First", default), new(10, "Again", default)], [], true)
        );
        Assert.Throws<ArgumentException>(() =>
            AccordionBatch.Create([new(10, "First", default)], [20u], true)
        );
        Assert.Throws<ArgumentException>(() =>
            AccordionBatch.Create(
                [new(10, "First", default), new(20, "Second", default)],
                [10u, 20u],
                false
            )
        );
        Assert.Throws<ArgumentException>(() =>
            AccordionBatch.Create([new(10, "\uD800", default)], [], false)
        );
    }

    [Fact]
    public void AccordionEventsDecodeTheCompleteOpenSet()
    {
        var changed = ComponentAccordionChangedEvent.Decode(
            new NativeExtensionEvent(
                ComponentSchema.Accordion.EventChanged,
                0,
                0,
                [10, 0, 0, 0, 20, 0, 0, 0]
            )
        );
        Assert.Equal([10u, 20u], changed.OpenIds);
        Assert.Throws<InvalidOperationException>(() =>
            ComponentAccordionChangedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Accordion.EventChanged, 0, 0, [10])
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            ComponentAccordionChangedEvent.Decode(
                new NativeExtensionEvent(
                    ComponentSchema.Accordion.EventChanged,
                    0,
                    0,
                    [10, 0, 0, 0, 10, 0, 0, 0]
                )
            )
        );
    }

    [Fact]
    public void DateBatchesValidateModesLimitsAndDisabledWeekdays()
    {
        var date = new DateOnly(2026, 9, 28);
        var batch = DateBatch.Create(
            new ComponentCalendarOptions
            {
                Value = ComponentDateValue.Single(date),
                MinimumDate = new(2026, 9, 1),
                MaximumDate = new(2026, 9, 30),
                DisabledWeekdays = [DayOfWeek.Saturday, DayOfWeek.Sunday],
            }
        );
        Assert.Equal((uint)date.DayNumber, batch.StartDay);
        Assert.Equal(uint.MaxValue, batch.EndDay);
        Assert.Equal((1u << 0) | (1u << 6), batch.DisabledWeekdays);
        Assert.Throws<ArgumentException>(() =>
            DateBatch.Create(new ComponentCalendarOptions { Value = new(false, date, date) })
        );
        Assert.Throws<ArgumentException>(() =>
            DateBatch.Create(
                new ComponentCalendarOptions
                {
                    Value = ComponentDateValue.Range(new(2026, 10, 2), new(2026, 10, 1)),
                }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            DateBatch.Create(
                new ComponentCalendarOptions
                {
                    Value = ComponentDateValue.Single(new(2026, 9, 26)),
                    DisabledWeekdays = [DayOfWeek.Saturday],
                }
            )
        );
    }

    [Fact]
    public void DateEventsDecodeSingleRangeAndClearing()
    {
        var single = ComponentDateChangedEvent.Decode(
            new NativeExtensionEvent(
                ComponentSchema.Calendar.EventChanged,
                0,
                0,
                [0, 0, 0, 0, 0, 255, 255, 255, 255]
            )
        );
        Assert.Equal(ComponentDateValue.Single(DateOnly.MinValue), single.Value);
        var cleared = ComponentDateChangedEvent.Decode(
            new NativeExtensionEvent(
                ComponentSchema.DatePicker.EventChanged,
                0,
                0,
                [1, 255, 255, 255, 255, 255, 255, 255, 255]
            )
        );
        Assert.Equal(ComponentDateValue.Range(null, null), cleared.Value);
        Assert.Throws<InvalidOperationException>(() =>
            ComponentDateChangedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Calendar.EventChanged, 0, 0, [0])
            )
        );
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
    public void TextareaCarriesInitialMultilineValueAndDecodesChanges()
    {
        Assert.Equal(
            "\"First\\nSecond\"\nNotes\n3\n0\n1\nReview notes\n17",
            ComponentSchema.Textarea.EncodeConfiguration(
                "First\nSecond",
                "Notes",
                3,
                false,
                true,
                "Review notes",
                17
            )
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ComponentElements.TextareaConfiguration(new ComponentTextareaOptions { Rows = 0 }, 0)
        );
        var changed = ComponentTextareaChangedEvent.Decode(
            new NativeExtensionEvent(
                ComponentSchema.Textarea.EventChanged,
                0,
                2,
                "Hello\n世界"u8.ToArray()
            )
        );
        Assert.Equal("Hello\n世界", changed.Value);
        Assert.Equal(2ul, changed.Revision);
        Assert.Throws<InvalidOperationException>(() =>
            ComponentTextareaChangedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Textarea.EventChanged, 0, 1, [0xff])
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            ComponentTextareaChangedEvent.Decode(
                new NativeExtensionEvent(ComponentSchema.Textarea.EventChanged, 0, 0, [])
            )
        );
    }

    [Fact]
    public void CoreFormFieldNamesItsControlAndDescribesRequirement()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var field = ComponentFormField.For(
            "Account",
            ui.Button("account-button"),
            helpText: "Use the account on your receipt",
            required: true
        );
        Assert.Equal("Account", field.Label);
        Assert.True(field.Required);
        arena.Validate(ui.Div(field.Control));
        Assert.Equal("Account", ReadDataOp(arena, OpCode.AccessibleName));
        Assert.Equal(
            "Required. Use the account on your receipt",
            ReadDataOp(arena, OpCode.AccessibleDescription)
        );
    }

    [Fact]
    public void FormRejectsInvalidLayoutAndDefaultFields()
    {
        Assert.Throws<ArgumentOutOfRangeException>(CheckColumns);
        Assert.Throws<ArgumentException>(CheckDefaultField);

        static void CheckColumns()
        {
            using var arena = new RenderArenaOwner();
            arena.BeginRender().Form("bad-form", [], new ComponentFormOptions { Columns = 0 });
        }

        static void CheckDefaultField()
        {
            using var arena = new RenderArenaOwner();
            arena.BeginRender().Form("bad-form", [default(ComponentFormField)]);
        }
    }

    [Fact]
    public void FormSchemaCarriesOneFieldBatch()
    {
        Assert.Equal(
            "medium\nvertical\n2\n140\n[\"Account\",\"Notes\"]\n[\"Help\",\"\"]\n[\"\",\"Error\"]\n1,0\n1,2\n1",
            ComponentSchema.Form.EncodeConfiguration(
                ComponentSchema.Form.Size.Medium,
                ComponentSchema.Form.LabelAxis.Vertical,
                2,
                140,
                ["Account", "Notes"],
                ["Help", ""],
                ["", "Error"],
                [1u, 0u],
                [1u, 2u],
                true
            )
        );
    }

    private static unsafe string ReadDataOp(RenderArenaOwner arena, OpCode code)
    {
        for (var index = arena.NativeArena->OpLength - 1; index >= 0; index--)
        {
            ref readonly var operation = ref arena.NativeArena->Ops[index];
            if (operation.Code != (ushort)code)
                continue;
            return System.Text.Encoding.UTF8.GetString(
                new ReadOnlySpan<byte>(
                    arena.NativeArena->Utf8 + (uint)operation.A,
                    checked((int)operation.B)
                )
            );
        }
        throw new Xunit.Sdk.XunitException($"The form field did not declare {code}.");
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
