namespace Gpui.Components;

internal static class ComponentSlots
{
    internal static Element[] Pack(params ReadOnlySpan<Element?> slots)
    {
        var count = 0;
        foreach (var slot in slots)
        {
            if (slot.HasValue)
                count++;
        }
        if (count == 0)
            return [];

        var children = new Element[count];
        var index = 0;
        foreach (var slot in slots)
        {
            if (slot.HasValue)
                children[index++] = slot.Value;
        }
        return children;
    }
}
