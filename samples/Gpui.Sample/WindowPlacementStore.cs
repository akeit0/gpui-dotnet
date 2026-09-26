using System.Text.Json;
using Gpui;

internal static class WindowPlacementStore
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Gpui.Sample",
        "window-placement.json"
    );

    internal static GpuiWindowPlacement? Load()
    {
        if (!File.Exists(PathName))
            return null;
        try
        {
            var placement = JsonSerializer.Deserialize<GpuiWindowPlacement>(
                File.ReadAllText(PathName)
            );
            if (
                float.IsFinite(placement.Left)
                && float.IsFinite(placement.Top)
                && float.IsFinite(placement.Width)
                && float.IsFinite(placement.Height)
                && placement.Width > 0
                && placement.Height > 0
                && Enum.IsDefined(placement.State)
            )
                return placement;
            Console.Error.WriteLine($"Ignoring invalid window placement in {PathName}.");
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"Could not read window placement from {PathName}: {exception.Message}"
            );
        }
        return null;
    }

    internal static void Save(GpuiWindowPlacement placement)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(placement));
    }
}
