using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;
namespace Hibike.App.Views;
internal static class Icons
{
    public static Control Make(string name, string color = UiTheme.Primary, double size = UiTheme.IconSize, bool filled = false)
    {
        // Fixed 24-unit viewport: never stretch a narrow path (pause/more) to a square.
        var path = new Path
        {
        Data = Geometry.Parse(name switch
        {
            "play" => "M8,4 L20,12 L8,20 Z",
            "home" => "M2,11 L12,2 L22,11 M5,9 L5,22 L10,22 L10,15 L14,15 L14,22 L19,22 L19,9",
            "pause" => "M7,4 L7,20 M17,4 L17,20",
            "next" => "M5,5 L16,12 L5,19 Z M20,4 L20,20",
            "previous" => "M19,5 L8,12 L19,19 Z M4,4 L4,20",
            "search" => "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 M15,15 L22,22",
            "heart" => "M12,21 C9,18 2,13 2,8 C2,2 9,1 12,6 C15,1 22,2 22,8 C22,13 15,18 12,21 Z",
            "queue" => "M3,5 L20,5 M3,11 L15,11 M3,17 L12,17 M18,14 L23,18 L18,22 Z",
            "library" => "M3,3 L3,21 L8,21 L8,3 Z M11,3 L11,21 L16,21 L16,3 Z M19,4 L23,20 M2,22 L23,22",
            "down" => "M5,8 L12,15 L19,8",
            "folder" => "M2,6 L9,6 L12,9 L22,9 L22,20 L2,20 Z",
            "shuffle" => "M3,5 L7,5 L17,19 L22,19 M18,15 L22,19 L18,23 M3,19 L7,19 L17,5 L22,5 M18,1 L22,5 L18,9",
            "repeat" => "M4,10 L4,6 L20,6 M16,2 L20,6 L16,10 M20,14 L20,18 L4,18 M8,14 L4,18 L8,22",
            "plus" => "M12,3 L12,21 M3,12 L21,12",
            "back" => "M15,4 L7,12 L15,20",
            "forward" => "M9,4 L17,12 L9,20",
            "more" => "M12,3 A1.5,1.5 0 1 0 12,6 A1.5,1.5 0 1 0 12,3 M12,10 A1.5,1.5 0 1 0 12,13 A1.5,1.5 0 1 0 12,10 M12,17 A1.5,1.5 0 1 0 12,20 A1.5,1.5 0 1 0 12,17",
            "settings" => "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M8,3 L8,9 M16,9 L16,15 M9,15 L9,21",
            "volume" => "M3,9 L7,9 L12,4 L12,20 L7,15 L3,15 Z M16,8 Q20,12 16,16 M19,4 Q26,12 19,20",
            "info" => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,10 L12,17 M12,6 L12,7",
            "close" => "M5,5 L19,19 M19,5 L5,19",
            _ => "M3,9 L3,15 M8,4 L8,20 M13,7 L13,17 M18,2 L18,22 M23,9 L23,15"
        }),
        Stretch = Stretch.None,
        Stroke = UiTheme.Brush(color), StrokeThickness = name == "pause" ? UiTheme.PauseStroke : UiTheme.IconStroke,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        Fill = filled || name is "play" or "more" ? UiTheme.Brush(color) : null,
        Margin = UiTheme.Zero
        };
        var canvas = new Canvas { Width = UiTheme.IconSize, Height = UiTheme.IconSize };
        canvas.Children.Add(path);
        return new Viewbox { Child = canvas, Width = size, Height = size, Stretch = Stretch.Uniform,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    }
}
