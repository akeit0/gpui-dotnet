namespace Gpui;

/// <summary>Read access to a reactive value. Rendering reads track a dependency.</summary>
/// <remarks>
/// Read-only access does not make a mutable value immutable. Bound Signal access remains confined
/// to its owning application thread.
/// </remarks>
public interface IReadOnlySignal<out T>
{
    /// <summary>Gets the current value, tracking a dependency during rendering.</summary>
    T Value { get; }
}
