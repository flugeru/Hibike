using Avalonia;
using Avalonia.Media;

namespace Hibiki.App.Views;

/// <summary>
/// Strongly typed access to Themes/DesignTokens.axaml. No duplicated layout values
/// in code-behind: both the Fluent overrides and the programmatic UI share tokens.
/// </summary>
internal static class Ui
{
    private static object Resource(string key)
    {
        var app = Application.Current ?? throw new InvalidOperationException("Application has not initialized.");
        if (app.TryGetResource(key, null, out var value) && value is not null)
            return value;
        throw new InvalidOperationException("Missing Hibiki design token: " + key);
    }

    public static T Get<T>(string key) where T : class => (T)Resource(key);
    public static BoxShadows Shadows(string key) => BoxShadows.Parse((string)Resource(key));
    public static double D(string key) => Convert.ToDouble(Resource(key));
    public static IBrush B(string key) => (IBrush)Resource(key);
    public static Color Color(string key) => ((ISolidColorBrush)B(key)).Color;
    public static CornerRadius Radius(string key) => new(D(key));
    public static Thickness Inset(string key) => new(D(key));
    public static Thickness Insets(string horizontal, string vertical) =>
        new(D(horizontal), D(vertical));
    public static Thickness Insets(string left, string top, string right, string bottom) =>
        new(D(left), D(top), D(right), D(bottom));
}
