using Gpui;

internal readonly record struct SignalReaderProps(
    IReadOnlySignal<int> Count,
    string Title,
    int Multiplier,
    bool CanPause
);
