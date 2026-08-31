using System.Diagnostics;
using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

const int Warmup = 2_000;
const int Iterations = 100_000;

using var arena = new RenderArenaOwner(
    nodeCapacity: 64,
    opCapacity: 256,
    childCapacity: 64,
    utf8Capacity: 4096
);

for (var i = 0; i < Warmup; i++)
{
    var ui = arena.BeginRender();
    _ = Build(ref ui);
}

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

var sw = new Stopwatch();
var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
sw.Start();

Element last = default;
for (var i = 0; i < Iterations; i++)
{
    var ui = arena.BeginRender();
    last = Build(ref ui);
}

sw.Stop();
var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

arena.Validate(last);
var stats = arena.GetStats();

Console.WriteLine($"iterations: {Iterations:N0}");
Console.WriteLine($"elapsed: {sw.Elapsed.TotalMilliseconds:N3} ms");
Console.WriteLine($"per render: {sw.Elapsed.TotalNanoseconds / Iterations:N1} ns");
Console.WriteLine(
    $"managed bytes allocated in measured loop: {allocatedAfter - allocatedBefore:N0}"
);
Console.WriteLine(
    $"last render: nodes={stats.Nodes}, ops={stats.Ops}, children={stats.Children}, utf8={stats.Utf8Bytes}"
);

static Element Build(ref RenderContext ui)
{
    const int iterationValue = 42;
    var title = ui.Text($"Performance probe {iterationValue, 6:D4}");
    var rowA = ui.HStack(ui.Text("A"u8), ui.Text("B"u8), ui.Text("C"u8)).Gap(Px(4)).ItemsCenter();
    var rowB = ui.HStack([ui.Text("D"u8), ui.Text("E"u8), ui.Text("F"u8)]).Gap(Px(4));

    var controls = ui.HStack(
            ui.Badge(ui.Text("LIVE"u8)),
            ui.Spacer(),
            ui.Checkbox("enabled", "Enabled"u8).Checked(true),
            ui.Radio("mode", "Mode"u8).Checked(true)
        )
        .Gap(Px(6))
        .ItemsCenter();

    return ui.VStack(title, rowA, rowB, ui.Divider(), controls)
        .Gap(Px(8))
        .Padding(Px(12))
        .Width(Percent(100))
        .BorderWidth(Px(1));
}
