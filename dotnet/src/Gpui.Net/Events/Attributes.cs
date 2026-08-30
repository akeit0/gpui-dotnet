namespace Gpui;

/// <summary>
/// Marks a partial <see cref="View"/> for AOT-safe framework child construction and compile-time
/// event dispatch generation. Generated views use a parameterless constructor; parent inputs belong
/// in <see cref="View{TProps}"/> props rather than constructor arguments.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GpuiViewAttribute : Attribute;

/// <summary>
/// Marks an instance method as a generated GPUI event handler. A handler may accept no arguments
/// or one <see cref="ClickEvent"/> and may return void, Task, or ValueTask. async void is rejected.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GpuiEventAttribute : Attribute;
