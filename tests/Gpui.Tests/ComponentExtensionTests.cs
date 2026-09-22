using System.Buffers.Binary;
using Gpui.Components;

namespace Gpui.Tests;

public sealed class ComponentExtensionTests
{
    [Fact]
    public void ComponentSchemaIdentityIsIndependentFromEditor()
    {
        Assert.Equal("gpui.net.components", ComponentsExtension.Requirement.Id);
        Assert.Equal(1u, ComponentsExtension.Requirement.Version);
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
}
