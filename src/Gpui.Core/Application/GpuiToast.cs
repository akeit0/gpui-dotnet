namespace Gpui;

/// <summary>A window notification. Showing the same ID again replaces the previous toast.</summary>
/// <param name="Id">Stable identifier within one window.</param>
/// <param name="Title">Primary message.</param>
/// <param name="Description">Optional detail.</param>
/// <param name="Timeout">Display time. Null uses five seconds; zero keeps the toast until dismissed.</param>
public sealed record GpuiToast(
    string Id,
    string Title,
    string? Description = null,
    TimeSpan? Timeout = null
);
