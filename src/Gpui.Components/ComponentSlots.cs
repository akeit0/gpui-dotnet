namespace Gpui.Components;

internal static class ComponentSlots
{
    internal static Element[] Pack(Element? first, Element? second, Element? third)
    {
        var count = (first.HasValue ? 1 : 0) + (second.HasValue ? 1 : 0) + (third.HasValue ? 1 : 0);
        if (count == 0)
            return [];

        var children = new Element[count];
        var index = 0;
        if (first.HasValue)
            children[index++] = first.Value;
        if (second.HasValue)
            children[index++] = second.Value;
        if (third.HasValue)
            children[index] = third.Value;
        return children;
    }
}
