namespace Gpui;

/// <summary>Marks a partial <see cref="View"/> for compile-time event dispatch generation.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GpuiViewAttribute : Attribute;

/// <summary>
/// Marks an instance method as a generated GPUI event handler. A handler may accept no arguments
/// or one <see cref="ClickEvent"/> and may return void, Task, or ValueTask. async void is rejected.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GpuiEventAttribute : Attribute;
