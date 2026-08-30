using System.Runtime.ExceptionServices;
using Gpui.Interop;

namespace Gpui;

public sealed class GpuiApplicationOptions
{
    public string Title { get; init; } = "GPUI.NET";
    public float Width { get; init; } = 720;
    public float Height { get; init; } = 480;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        if (!float.IsFinite(Width) || Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }
        if (!float.IsFinite(Height) || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Height));
        }
    }
}

public static class GpuiApplication
{
    /// <summary>Runs one managed view on the current thread until its native window closes.</summary>
    public static void Run(View view, GpuiApplicationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        options ??= new GpuiApplicationOptions();
        options.Validate();

        if (
            OperatingSystem.IsWindows()
            && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA
        )
        {
            Exception? failure = null;
            var uiThread = new Thread(() =>
            {
                try
                {
                    NativeRuntime.Load().Run(view, options);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            })
            {
                IsBackground = false,
                Name = "GPUI UI",
            };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            uiThread.Join();

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return;
        }

        NativeRuntime.Load().Run(view, options);
    }
}
