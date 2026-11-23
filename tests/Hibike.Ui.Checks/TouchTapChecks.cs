using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Hibike.App;
using Hibike.App.Views;
using Avalonia.Headless;

internal static partial class Program
{
    // Regression: moving through touch lists used to start playback. A short drag or a touch that
    // stops a moving list must not click the row under the finger; a still tap and mouse input must.
    private static async Task CheckTouchTapGuardAsync(Window window)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var music = AppServices.Controller!;
        var previous = window.Content;
        var view = (MainView)Activator.CreateInstance(typeof(MainView), hidden, null, [music, true], null)!;
        window.Width = 390; window.Height = 760; window.Content = view;
        try
        {
            await Task.Delay(160);
            var home = (ScrollViewer)typeof(MainView).GetField("_homeScroll", hidden)!.GetValue(view)!;
            var clicks = 0;
            var rows = new StackPanel();
            for (var i = 0; i < 40; i++)
            {
                var row = new Button { Content = "Row " + i, Height = 64, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
                row.Click += (_, _) => clicks++;
                rows.Children.Add(row);
            }
            home.Content = rows;
            view.UpdateLayout();
            // Button clicks hit-test the rendered scene; render a frame before simulating input.
            window.CaptureRenderedFrame();
            var target = (Button)rows.Children[3];
            var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
            var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var released = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
            var id = 400;
            void Gesture(PointerType type, Vector travel)
            {
                var pointer = new Avalonia.Input.Pointer(id++, type, true);
                // Touch and mouse devices capture the pressed element before raising PointerPressed.
                pointer.Capture(target);
                target.RaiseEvent(new PointerPressedEventArgs(target, pointer, window, center, 1, pressed, KeyModifiers.None));
                var end = center + travel;
                if (travel != default)
                    target.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, target, pointer, window, end, 2, pressed, KeyModifiers.None));
                // Release over the original point: the finger came back, but it still scrolled.
                target.RaiseEvent(new PointerReleasedEventArgs(target, pointer, window, travel == default ? center : center + travel / 4, 3,
                    released, KeyModifiers.None, MouseButton.Left));
            }

            Gesture(PointerType.Touch, default);
            Check(clicks == 1, $"A still touch tap did not activate the row ({clicks})");
            Gesture(PointerType.Touch, new Vector(0, 22));
            Check(clicks == 1, "A short vertical drag activated the row under the finger");
            Gesture(PointerType.Touch, new Vector(18, 0));
            Check(clicks == 1, "A short horizontal drag activated the row under the finger");

            home.Offset = new Vector(0, 40);
            view.UpdateLayout();
            window.CaptureRenderedFrame();
            center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
            Gesture(PointerType.Touch, default);
            Check(clicks == 1, "Touching a moving list to stop it also activated a row");
            await Task.Delay(260);
            Gesture(PointerType.Touch, default);
            Check(clicks == 2, "A tap after the list settled was ignored");
            Gesture(PointerType.Mouse, new Vector(0, 22));
            Check(clicks == 3, "Desktop mouse clicks changed behaviour");
        }
        finally { window.Content = previous; }
    }
}
