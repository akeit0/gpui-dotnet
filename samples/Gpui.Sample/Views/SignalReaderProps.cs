using Gpui;

internal readonly record struct SignalReaderProps(
    Signal<int> Count,
    string Title,
    int Multiplier,
    bool CanPause
);
