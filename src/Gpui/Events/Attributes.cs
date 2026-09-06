namespace Gpui;

/// <summary>
/// Generates AOT-safe root/child factories, typed Spec declarations, and virtual-row dispatch.
/// Constructors receive ViewConstruction and initial props; subsequent declarations supply render inputs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GpuiViewAttribute : Attribute;

/// <summary>
/// Marks a synchronous virtualized list-item renderer. The method signature is
/// Element Method(int index, ref RenderContext ui), with an additional in TProps argument before ui
/// for props-bearing Views. The source generator emits an allocation-free
/// renderer token and dispatch switch used by native range virtualization.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GpuiListItemAttribute : Attribute;
