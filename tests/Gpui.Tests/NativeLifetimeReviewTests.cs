using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe class NativeLifetimeReviewTests
{
    private static WeakReference? _owner;
    private static bool _aliveDuringCall;

    [Fact]
    public void StandaloneValidationKeepsArenaAliveDuringNativeCall()
    {
        var api = new GpuiDotnetApiV3 { validate_render = &CollectDuringValidation };
        var constructor = typeof(NativeRuntime)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        var runtime = (NativeRuntime)
            constructor.Invoke([Pointer.Box(&api, typeof(GpuiDotnetApiV3*)), null]);
        ValidateLastUse(runtime);
        Assert.True(_aliveDuringCall);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(_owner!.IsAlive);
        using var disposed = new RenderArenaOwner();
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => runtime.Validate(disposed, default));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidateLastUse(NativeRuntime runtime)
    {
        var owner = new RenderArenaOwner();
        _owner = new WeakReference(owner);
        var ui = owner.BeginRender();
        runtime.Validate(owner, ui.Text("alive"));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CollectDuringValidation(RenderArena* arena, uint root)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _aliveDuringCall = _owner!.IsAlive;
        return _aliveDuringCall && arena->Nodes[root].DataLength == 5 ? 0 : -1;
    }
}
