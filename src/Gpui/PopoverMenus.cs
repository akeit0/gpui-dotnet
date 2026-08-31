namespace Gpui;

/// <summary>Native trigger-attached menu placement and dismissal behavior.</summary>
public readonly struct PopoverMenuOptions
{
    private const uint DefaultPriority = 300;
    private const float DefaultMargin = 8;
    private readonly bool _initialized;

    public PopoverMenuOptions(uint priority = DefaultPriority, float margin = DefaultMargin)
    {
        if (!float.IsFinite(margin) || margin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(margin));
        }

        Priority = priority;
        Margin = margin;
        _initialized = true;
    }

    public uint Priority { get; }
    public float Margin { get; }

    internal uint EffectivePriority => _initialized ? Priority : DefaultPriority;
    internal float EffectiveMargin => _initialized ? Margin : DefaultMargin;
}
