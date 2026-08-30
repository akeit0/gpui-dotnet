namespace Gpui;

/// <summary>
/// Allocation-free event binding emitted by the GPUI source generator. The token contains
/// the mounted view identity in the high 32 bits and the generated handler id in the low 32 bits.
/// </summary>
public readonly struct Callback<TEvent>
{
    internal Callback(ulong token) => Token = token;

    internal ulong Token { get; }

    public bool IsDefault => Token == 0;
}

public struct ClickEvent
{
    /// <summary>Optional unmanaged payload captured when the event binding is rendered.</summary>
    public ulong Payload;
    public float X;
    public float Y;
    public uint Buttons;
    public uint Modifiers;
}

/// <summary>
/// Allocation-free token identifying a generated virtualized list-item renderer on a mounted View.
/// </summary>
public readonly struct ListItemRenderer
{
    internal ListItemRenderer(ulong token) => Token = token;

    internal ulong Token { get; }

    public bool IsDefault => Token == 0;
}
