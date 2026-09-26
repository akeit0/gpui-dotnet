using Gpui.Interop.Internal;

namespace Gpui;

/// <summary>A typed declaration. Creating or copying it does not construct a View.</summary>
public readonly struct ViewSpec<TView>
    where TView : View, IGeneratedViewFactory<TView>
{
    internal TView Create(GpuiWindow? window = null) => ViewFactory.Create<TView>(window);
}

/// <summary>Current inputs for a framework-owned View; use immutable equatable input values.</summary>
public readonly struct ViewSpec<TView, TProps>(TProps props)
    where TProps : IEquatable<TProps>
    where TView : View<TProps>, IGeneratedViewFactory<TView, TProps>
{
    internal TProps Props { get; } = props;

    internal TView Create(GpuiWindow? window = null) =>
        ViewFactory.Create<TView, TProps>(Props, window);
}

internal abstract class RootViewDeclaration
{
    internal abstract ViewBase Create(GpuiWindow window);
}

internal sealed class RootViewDeclaration<TView>(ViewSpec<TView> spec) : RootViewDeclaration
    where TView : View, IGeneratedViewFactory<TView>
{
    internal override ViewBase Create(GpuiWindow window) => spec.Create(window);
}

internal sealed class RootViewDeclaration<TView, TProps>(ViewSpec<TView, TProps> spec)
    : RootViewDeclaration
    where TProps : IEquatable<TProps>
    where TView : View<TProps>, IGeneratedViewFactory<TView, TProps>
{
    internal override ViewBase Create(GpuiWindow window) => spec.Create(window);
}

internal static class ViewFactory
{
    internal static TView Create<TView>(GpuiWindow? window = null)
        where TView : View, IGeneratedViewFactory<TView> =>
        Construct<TView, NoProps>(
            default,
            static (context, _) => TView.CreateGpuiView(context),
            window
        );

    internal static TView Create<TView, TProps>(TProps props, GpuiWindow? window = null)
        where TProps : IEquatable<TProps>
        where TView : View<TProps>, IGeneratedViewFactory<TView, TProps>
    {
        var view = Construct<TView, TProps>(
            props,
            static (context, input) => TView.CreateGpuiView(context, input),
            window
        );
        view.StageProps(props);
        return view;
    }

    internal delegate TView Constructor<TView, TInput>(ViewConstruction context, TInput input);

    internal static TView Construct<TView, TInput>(
        TInput input,
        Constructor<TView, TInput> create,
        GpuiWindow? window = null
    )
        where TView : ViewBase
    {
        var owner = new ViewOwnership { Window = window };
        var previousReads = ReactiveConsumer.Current;
        var previousConstruction = ViewOwnership.Constructing;
        ReactiveConsumer.Current = null;
        ViewOwnership.Constructing = true;
        try
        {
            var view = create(new ViewConstruction(owner), input);
            if (view is null || !ReferenceEquals(owner.View, view))
                throw new InvalidOperationException(
                    "The factory must return the View bound to its construction context."
                );
            owner.ConstructionComplete = true;
            return view;
        }
        catch (Exception failure)
        {
            try
            {
                if (owner.View is { } view)
                    view.Runtime.UnmountRuntime();
                else
                    owner.Retire();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(failure, cleanup);
            }
            throw;
        }
        finally
        {
            ReactiveConsumer.Current = previousReads;
            ViewOwnership.Constructing = previousConstruction;
        }
    }
}

/// <summary>Constant input for an effect that lasts for its accepted declaration's lifetime.</summary>
public readonly record struct NoProps;
