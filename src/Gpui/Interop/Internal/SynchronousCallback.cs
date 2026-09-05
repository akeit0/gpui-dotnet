using System.Reflection;
using System.Runtime.CompilerServices;

namespace Gpui.Interop.Internal;

internal static class SynchronousCallback
{
    private sealed class MethodContract(bool isAsync)
    {
        internal static readonly MethodContract Synchronous = new(false);
        internal static readonly MethodContract Asynchronous = new(true);
        internal bool IsAsync { get; } = isAsync;
    }

    private static readonly ConditionalWeakTable<MethodInfo, MethodContract> Contracts = new();

    internal static void Validate(Delegate callback)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(callback))
        {
            var contract = Contracts.GetValue(handler.Method, static method =>
                method.IsDefined(typeof(AsyncStateMachineAttribute), inherit: false)
                    ? MethodContract.Asynchronous : MethodContract.Synchronous);
            if (contract.IsAsync)
                throw new InvalidOperationException(
                    "Callbacks must be synchronous. Use WorkScope.Start for asynchronous production.");
        }
    }
}
