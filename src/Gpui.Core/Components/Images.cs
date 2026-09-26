namespace Gpui;

/// <summary>Controls how an image is fitted into its layout bounds.</summary>
public enum ImageFit : uint
{
    Fill = 0,
    Contain = 1,
    Cover = 2,
    ScaleDown = 3,
    None = 4,
}
