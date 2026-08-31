// uidrive: minimal Win32 UI driver for manual QA of the sample app.
//
// Commands (coordinates are logical client-space px, scaled by the window's DPI):
//   uidrive find [title]                     print window handle/rect/dpi
//   uidrive click <lx> <ly> [title]          left click
//   uidrive wheel <lx> <ly> <notches> [title] wheel scroll (negative = down)
//   uidrive shot <out.png> [title]           capture the client area to a PNG
//
// The window is matched by title substring; the default matches the sample shell.

using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Gpui.Tools;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventWheel = 0x0800;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private static int Main(string[] args)
    {
        _ = SetProcessDPIAware();

        // Parse: uidrive <command> [positionals...] [-t|--title <substring>]
        string? title = null;
        var positionals = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "-t" or "--title")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("uidrive: --title requires a value.");
                    return 1;
                }
                title = args[++i];
            }
            else
            {
                positionals.Add(args[i]);
            }
        }

        if (positionals.Count == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = positionals[0].ToLowerInvariant();
        var windowTitle = title ?? "GPUI.NET";

        switch (command)
        {
            case "find":
                return WithWindow(
                    windowTitle,
                    hWnd =>
                    {
                        var (origin, client, scale) = ClientMetrics(hWnd);
                        Console.WriteLine(
                            $"hwnd=0x{hWnd.ToInt64():X} origin=({origin.X},{origin.Y}) "
                                + $"client={client.Right}x{client.Bottom} dpiScale={scale}"
                        );
                        return 0;
                    }
                );
            case "click" when positionals.Count >= 3:
                return WithWindow(
                    windowTitle,
                    hWnd => Click(hWnd, int.Parse(positionals[1]), int.Parse(positionals[2]))
                );
            case "wheel" when positionals.Count >= 4:
                return WithWindow(
                    windowTitle,
                    hWnd =>
                        Wheel(
                            hWnd,
                            int.Parse(positionals[1]),
                            int.Parse(positionals[2]),
                            int.Parse(positionals[3])
                        )
                );
            case "shot" when positionals.Count >= 2:
                return WithWindow(windowTitle, hWnd => Shot(hWnd, positionals[1]));
            default:
                PrintUsage();
                return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "usage: uidrive find [title] | click <lx> <ly> [title] | "
                + "wheel <lx> <ly> <notches> [title] | shot <out.png> [title]"
        );
    }

    private static IntPtr FindWindowByTitle(string substring)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(
            (hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }
                var text = new StringBuilder(512);
                _ = GetWindowTextW(hWnd, text, 512);
                if (text.ToString().Contains(substring, StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    return false;
                }
                return true;
            },
            IntPtr.Zero
        );
        return found;
    }

    private static int WithWindow(string title, Func<IntPtr, int> action)
    {
        var hWnd = FindWindowByTitle(title);
        if (hWnd == IntPtr.Zero)
        {
            Console.Error.WriteLine($"uidrive: window matching '{title}' not found.");
            return 1;
        }
        return action(hWnd);
    }

    private static (Point Origin, Rect Client, double Scale) ClientMetrics(IntPtr hWnd)
    {
        var client = new Rect();
        _ = GetClientRect(hWnd, out client);
        var origin = new Point();
        _ = ClientToScreen(hWnd, ref origin);
        var scale = GetDpiForWindow(hWnd) / 96.0;
        return (origin, client, scale);
    }

    private static void SetCursorLogical(IntPtr hWnd, double lx, double ly)
    {
        var (origin, _, scale) = ClientMetrics(hWnd);
        _ = SetCursorPos(
            (int)Math.Round(origin.X + lx * scale),
            (int)Math.Round(origin.Y + ly * scale)
        );
    }

    private static int Click(IntPtr hWnd, int lx, int ly)
    {
        _ = SetForegroundWindow(hWnd);
        Thread.Sleep(60);
        SetCursorLogical(hWnd, lx, ly);
        Thread.Sleep(60);
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        return 0;
    }

    private static int Wheel(IntPtr hWnd, int lx, int ly, int notches)
    {
        SetCursorLogical(hWnd, lx, ly);
        Thread.Sleep(60);
        var delta = unchecked((uint)(notches * -120));
        mouse_event(MouseEventWheel, 0, 0, delta, UIntPtr.Zero);
        return 0;
    }

    private static int Shot(IntPtr hWnd, string path)
    {
        var (origin, client, _) = ClientMetrics(hWnd);
        var width = client.Right;
        var height = client.Bottom;
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"saved {path} ({width}x{height})");
        return 0;
    }
}
