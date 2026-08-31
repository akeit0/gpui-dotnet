namespace Gpui;

// Shared resource-command wire types. The per-resource controllers and option structs live in
// Scrolling.cs, Lists.cs, Inputs.cs, and Tables.cs.

internal enum ResourceKind : ushort
{
    Scroll = 1,
    List = 2,
    Input = 3,
}

internal enum ResourceCommandKind : ushort
{
    ScrollToOffset = 1,
    ScrollToTop = 2,
    ScrollToBottom = 3,
    ListScrollToItem = 10,
    ListSplice = 11,
    ListReset = 12,
    ListRefresh = 13,
    InputFocus = 20,
    InputBlur = 21,
    InputSetValue = 22,
    InputSelectAll = 23,
}

internal readonly record struct ResourceCommand(
    ResourceKind ResourceKind,
    ResourceCommandKind Command,
    string? Key,
    ulong A,
    ulong B,
    string? Data = null,
    byte[]? Utf8Key = null
);
