using System.Reflection;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;

internal static partial class Program
{
    // Regression: the avatar menu opened at zero height on first use (its height came from the
    // still-hidden layer) and, on close, disabled every item (40 % opacity) before fading out.
    private static async Task CheckTopMenuMotionAsync(Window window)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var music = AppServices.Controller!;
        var previous = window.Content;
        var previousMotion = music.ReduceMotion;
        var view = (MainView)Activator.CreateInstance(typeof(MainView), hidden, null, [music, true], null)!;
        window.Width = 390; window.Height = 844; window.Content = view;
        try
        {
            music.SetReduceMotion(false);
            await Task.Delay(200);
            var layer = (Grid)typeof(MainView).GetField("_topMenuLayer", hidden)!.GetValue(view)!;
            var panel = (Border)typeof(MainView).GetField("_topMenuPanel", hidden)!.GetValue(view)!;
            void Toggle() => typeof(MainView).GetMethod("ToggleTopMenu", hidden)!.Invoke(view, null);

            Toggle();
            view.UpdateLayout();
            Check(layer.IsVisible && panel.Bounds.Height > 200, $"Menu opened without its final height ({panel.Bounds.Height:0})");
            await Task.Delay(260);
            Check(layer.Opacity == 1 && panel.Opacity == 1, "Menu did not finish fading in");
            var items = panel.GetVisualDescendants().OfType<Button>().ToArray();
            Check(items.Length >= 4, "Menu items missing");

            Toggle();
            Check(items.All(item => item.IsEffectivelyEnabled), "Closing the menu disabled (dimmed) its items before the fade");
            Check(!layer.IsHitTestVisible, "Closing menu still captures taps");
            await Task.Delay(300);
            Check(!layer.IsVisible, "Menu layer stayed visible after closing");

            // Reopening right after closing must end open, not hidden by the earlier close.
            Toggle(); Toggle(); Toggle();
            await Task.Delay(300);
            Check(layer.IsVisible && layer.Opacity == 1 && panel.Opacity == 1, "Rapid toggling left the menu in a broken state");
            Toggle();
            await Task.Delay(300);
        }
        finally
        {
            window.Content = previous;
            music.SetReduceMotion(previousMotion);
        }
    }
}
