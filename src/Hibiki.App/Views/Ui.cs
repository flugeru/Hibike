using Avalonia;
using Avalonia.Media;

namespace Hibiki.App.Views;

internal static class Ui
{
    private static T Resource<T>(string key)
    {
        var app = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");
        if (app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is T result)
            return result;
        throw new InvalidOperationException($"Missing or invalid UI resource: {key}.");
    }

    public static IBrush Brush(string key) => Resource<IBrush>(key);
    public static Color Color(string key) => Resource<Color>(key);
    public static double Size(string key) => Resource<double>(key);
    public static CornerRadius Radius(string key) => Resource<CornerRadius>(key);
    public static Thickness Insets(string key) => Resource<Thickness>(key);
}
