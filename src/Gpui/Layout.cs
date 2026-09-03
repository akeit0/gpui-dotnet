namespace Gpui;

/// <summary>Flex wrapping behavior of a container.</summary>
public enum FlexWrap : uint
{
    NoWrap = 0,
    Wrap = 1,
    WrapReverse = 2,
}

/// <summary>Horizontal text alignment within an element.</summary>
public enum TextAlignment : uint
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>Mouse cursor shown while hovering an element.</summary>
public enum MouseCursor : uint
{
    Arrow = 0,
    IBeam = 1,
    Crosshair = 2,
    ClosedHand = 3,
    OpenHand = 4,
    PointingHand = 5,
    ResizeLeft = 6,
    ResizeRight = 7,
    ResizeLeftRight = 8,
    ResizeUp = 9,
    ResizeDown = 10,
    ResizeUpDown = 11,
    ResizeUpLeftDownRight = 12,
    ResizeUpRightDownLeft = 13,
    ResizeColumn = 14,
    ResizeRow = 15,
    IBeamCursorForVerticalLayout = 16,
    OperationNotAllowed = 17,
    DragLink = 18,
    DragCopy = 19,
    ContextualMenu = 20,
}
