extern alias Wander;

using Gpui.Interop;
using Travel = Wander::Gpui;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void WanderTabsInheritNavigationForegroundAcrossSelectionAndThemeChanges()
    {
        var application = new GpuiApplication();
        var spec = Travel.WanderShellView.Spec();
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(
            null,
            application,
            new RootViewDeclaration<Travel.WanderShellView>(spec),
            window
        );

        foreach (
            var theme in new[]
            {
                Travel.WanderThemes.Light,
                Travel.WanderThemes.Dark,
                Travel.WanderThemes.Light,
            }
        )
        {
            application.SetTheme(theme);
            for (var selected = 0; selected < 4; selected++)
            {
                Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
                var ops = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray();
                var children = new ReadOnlySpan<ChildRecord>(
                    arena.Children,
                    arena.ChildLength
                ).ToArray();
                for (var tab = 0; tab < 4; tab++)
                {
                    var key = System.Text.Encoding.UTF8.GetBytes($"tab-{tab}");
                    var root = Enumerable
                        .Range(0, arena.NodeLength)
                        .Single(index =>
                            arena.Nodes[index].Component == (ushort)ComponentId.Button
                            && new ReadOnlySpan<byte>(
                                arena.Utf8 + arena.Nodes[index].DataOffset,
                                (int)arena.Nodes[index].DataLength
                            ).SequenceEqual(key)
                        );
                    var descendants = new HashSet<uint>();
                    var pending = new Queue<uint>();
                    pending.Enqueue((uint)root);
                    while (pending.TryDequeue(out var parent))
                    {
                        foreach (var child in children.Where(child => child.Parent == parent))
                        {
                            descendants.Add(child.Child);
                            pending.Enqueue(child.Child);
                        }
                    }
                    Assert.Equal(
                        3,
                        descendants.Count(id =>
                            arena.Nodes[id].Component == (ushort)ComponentId.Text
                        )
                    );
                    Assert.DoesNotContain(
                        ops,
                        op => descendants.Contains(op.Node) && op.Code == (ushort)OpCode.TextRgba
                    );
                    var foreground = new Color(
                        (uint)
                            ops.Last(op => op.Node == root && op.Code == (ushort)OpCode.TextRgba).A
                    );
                    var background = new Color(
                        (uint)
                            ops.Last(op =>
                                op.Node == root && op.Code == (ushort)OpCode.BackgroundRgba
                            ).A
                    );
                    var expected = Travel.WanderStyles.Button(
                        theme,
                        Travel.WanderButtonVariant.Navigation,
                        tab == selected
                    );
                    Assert.Equal(expected.Colors.Normal.Foreground, foreground);
                    Assert.Equal(expected.Colors.Normal.Background, background);
                    Assert.NotEqual(theme.Colors.Text, background);
                    ContrastAssert.OpaqueText(foreground, background, $"{theme.Name}, tab {tab}");
                }
                var click = SampleButtonClick(
                    arena,
                    System.Text.Encoding.UTF8.GetBytes($"tab-{(selected + 1) % 4}")
                );
                Assert.Equal(0, fixture.Complete(revision));
                Assert.Equal(0, fixture.Click(click, (ulong)((selected + 1) % 4)));
            }
        }
        Assert.Null(fixture.Session.Failure);
    }
}
