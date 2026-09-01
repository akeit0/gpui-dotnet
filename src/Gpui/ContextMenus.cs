namespace Gpui;

/// <summary>Native right-click menu placement and deferred-layer behavior.</summary>
public readonly struct ContextMenuOptions
{
    private const uint DefaultPriority = 300;
    private const float DefaultMargin = 8;
    private readonly bool _initialized;

    public ContextMenuOptions(uint priority = DefaultPriority, float margin = DefaultMargin)
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
