namespace Gpui;

/// <summary>
/// Marks a partial <see cref="View"/> or <see cref="View{TProps}"/> for AOT-safe framework child
/// construction and compile-time event dispatch generation. Generated views use an internal
/// parameterless factory; parent inputs belong in the required props child declaration rather than
/// constructor arguments.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GpuiViewAttribute : Attribute;

/// <summary>
/// Marks a synchronous virtualized list-item renderer. The method signature is
/// Element Method(int index, ref RenderContext ui). The source generator emits an allocation-free
/// renderer token and dispatch switch used by native range virtualization.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GpuiListItemAttribute : Attribute;
