using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hibiki.App.Views;

/// <summary>
/// Filled Material Symbols Rounded-style icons, supplied through one PathIcon
/// factory so all UI actions have the same 24dp baseline and color behavior.
/// </summary>
internal static class Icons
{
    public static PathIcon Make(string name, string color = "TextSecondaryBrush", double size = 24)
    {
        var geometry = name switch
        {
            "play" => "M8,5 Q8,4 9,4.5 L20,11 Q21,12 20,13 L9,19.5 Q8,20 8,19 Z",
            "pause" => "M7,4 Q6,4 6,5 L6,19 Q6,20 7,20 L9,20 Q10,20 10,19 L10,5 Q10,4 9,4 Z M15,4 Q14,4 14,5 L14,19 Q14,20 15,20 L17,20 Q18,20 18,19 L18,5 Q18,4 17,4 Z",
            "next" => "M5,5 L5,19 L16,12 Z M18,5 L18,19 L20,19 L20,5 Z",
            "previous" => "M19,5 L19,19 L8,12 Z M4,5 L4,19 L6,19 L6,5 Z",
            "search" => "M15.5,14 L14.71,14 L14.43,13.73 A6.4,6.4 0 1 0 13.73,14.43 L14,14.71 L14,15.5 L19,20 L20,19 Z M10,14 A4,4 0 1 1 10,6 A4,4 0 1 1 10,14 Z",
            "heart" => "M12,21.35 L10.55,20.03 C5.4,15.36 2,12.28 2,8.5 C2,5.42 4.42,3 7.5,3 C9.24,3 10.91,3.81 12,5.09 C13.09,3.81 14.76,3 16.5,3 C19.58,3 22,5.42 22,8.5 C22,12.28 18.6,15.36 13.45,20.04 Z",
            "heart-outline" => "M12,21.35 L10.55,20.03 C5.4,15.36 2,12.28 2,8.5 C2,5.42 4.42,3 7.5,3 C9.24,3 10.91,3.81 12,5.09 C13.09,3.81 14.76,3 16.5,3 C19.58,3 22,5.42 22,8.5 C22,12.28 18.6,15.36 13.45,20.04 Z M12,18.65 C16.76,14.33 20,11.4 20,8.5 C20,6.5 18.5,5 16.5,5 C14.96,5 13.47,5.99 12.94,7.36 L11.06,7.36 C10.53,5.99 9.04,5 7.5,5 C5.5,5 4,6.5 4,8.5 C4,11.4 7.24,14.33 12,18.65 Z",
            "queue" => "M3,5 L21,5 L21,7 L3,7 Z M3,10 L16,10 L16,12 L3,12 Z M3,15 L12,15 L12,17 L3,17 Z M17,14 L17,22 L23,18 Z",
            "library" => "M3,3 L5,3 L5,21 L3,21 Z M7,5 L9,5 L9,21 L7,21 Z M11,3 L16,3 L16,21 L11,21 Z M18,7 L20,7 L22,21 L20,21 Z",
            "folder" => "M10,4 L2,4 Q1,4 1,5 L1,19 Q1,20 2,20 L22,20 Q23,20 23,19 L23,7 Q23,6 22,6 L12,6 Z M3,8 L21,8 L21,18 L3,18 Z",
            "shuffle" => "M17,3 L22,3 L22,8 L20,8 L20,6.4 L17,9.4 L15.6,8 L18.6,5 L17,5 Z M3,6 L8,6 L17.2,15.2 L20,18 L20,16 L22,16 L22,21 L17,21 L17,19 L18.6,19 L6.9,8 L3,8 Z M3,18 L7,18 L10.6,14.4 L12,15.8 L8,20 L3,20 Z M17,5 L22,5 L22,7 L17,7 Z",
            "repeat" => "M7,7 L17,7 L17,5 L21,9 L17,13 L17,10 L7,10 Q5,10 5,12 L3,12 Q3,7 7,7 Z M17,17 L7,17 L7,19 L3,15 L7,11 L7,14 L17,14 Q19,14 19,12 L21,12 Q21,17 17,17 Z",
            "repeat-one" => "M7,7 L17,7 L17,5 L21,9 L17,13 L17,10 L7,10 L7,8 Z M17,17 L7,17 L7,19 L3,15 L7,11 L7,14 L17,14 L17,16 Z M11,10 L14,10 L14,15 L12,15 L12,12 L11,12 Z",
            "chevron-down" => "M7.4,9 L12,13.6 L16.6,9 L18,10.4 L12,16.4 L6,10.4 Z",
            "more" => "M12,4 A2,2 0 1 0 12,8 A2,2 0 1 0 12,4 M12,10 A2,2 0 1 0 12,14 A2,2 0 1 0 12,10 M12,16 A2,2 0 1 0 12,20 A2,2 0 1 0 12,16",
            "settings" => "M4,6 L20,6 L20,8 L4,8 Z M4,12 L20,12 L20,14 L4,14 Z M4,18 L20,18 L20,20 L4,20 Z M8,4 L10,4 L10,10 L8,10 Z M15,10 L17,10 L17,16 L15,16 Z M9,16 L11,16 L11,22 L9,22 Z",
            "info" => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M11,11 L13,11 L13,17 L11,17 Z M11,7 L13,7 L13,9 L11,9 Z",
            "close" => "M18.3,5.7 L16.9,4.3 L12,9.2 L7.1,4.3 L5.7,5.7 L10.6,10.6 L5.7,15.5 L7.1,16.9 L12,12 L16.9,16.9 L18.3,15.5 L13.4,10.6 Z",
            "equalizer" => "M3,9 L6,9 L6,15 L3,15 Z M8,4 L11,4 L11,20 L8,20 Z M13,7 L16,7 L16,17 L13,17 Z M18,2 L21,2 L21,22 L18,22 Z",
            _ => "M2,10 L5,10 L5,14 L2,14 Z M7,4 L10,4 L10,20 L7,20 Z M12,7 L15,7 L15,17 L12,17 Z M17,2 L20,2 L20,22 L17,22 Z"
        };
        return new PathIcon
        {
            Data = Geometry.Parse(geometry),
            Foreground = Ui.B(color.StartsWith("#", StringComparison.Ordinal)
                ? "TextSecondaryBrush" : color),
            Width = size,
            Height = size,
            Margin = Ui.Inset("Space0")
        };
    }
}
