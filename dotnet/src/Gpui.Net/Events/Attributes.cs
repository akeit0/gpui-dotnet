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
/// or one <see cref="ClickEvent"/>/<see cref="InputEvent"/> and may return void, Task, or ValueTask.
/// A parameterless handler binds as a click handler; input handlers must accept InputEvent.
/// async void is rejected.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GpuiEventAttribute : Attribute;

/// <summary>
/// Marks a synchronous virtualized list-item renderer. The method signature is
/// Element Method(int index, ref RenderContext ui). The source generator emits an allocation-free
/// renderer token and dispatch switch used by native range virtualization.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GpuiListItemAttribute : Attribute;
