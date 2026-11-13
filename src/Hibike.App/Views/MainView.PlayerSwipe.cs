using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private const double PlayerSwipeSlop = 10;
    private const double PlayerSwipeVelocity = 650;
    private static readonly TimeSpan PlayerSlideDuration = TimeSpan.FromMilliseconds(240);
    private IPointer? _playerPointer;
    private Point _playerPointerOrigin;
    private Point _playerPointerLast;
    private ulong _playerPointerTime;
    private double _playerVelocity;
    private bool _playerDragFromMini;
    private bool _playerDragging;
    private double _playerDragStart;
    private Border? _slidingOutPlayer;

    private void InitializePlayerSwipe()
    {
        if (!_androidLayout) return;
        _root.ClipToBounds = true;
        _root.AddHandler(PointerPressedEvent, BeginPlayerSwipe, RoutingStrategies.Tunnel, handledEventsToo: true);
        _root.AddHandler(PointerMovedEvent, MovePlayerSwipe, RoutingStrategies.Tunnel, handledEventsToo: true);
        _root.AddHandler(PointerReleasedEvent, ReleasePlayerSwipe, RoutingStrategies.Tunnel, handledEventsToo: true);
        _root.PointerCaptureLost += (_, e) =>
        {
            if (_playerPointer == e.Pointer && _playerDragging)
                FinishPlayerSwipe(e.Pointer, commit: false);
        };
    }

    private void BeginPlayerSwipe(object? sender, PointerPressedEventArgs e)
    {
        if (_playerPointer is not null || e.Pointer.Type != PointerType.Touch || _overlay is not null
            || _googleWelcome is not null || _onboarding is not null || _topMenuOpen || _songPopup is { IsOpen: true }
            || e.Source is not Visual source) return;

        var surface = _showingPlayer ? _playerPage : _mini;
        if (surface is not { IsEffectivelyVisible: true } || source != surface && !surface.IsVisualAncestorOf(source)) return;

        // Keep transport buttons and the seek bar independent of the player drag.
        var controls = source.GetSelfAndVisualAncestors().TakeWhile(visual => visual != surface);
        if (controls.Any(visual => visual is Slider or ScrollViewer
            || visual is Button button && (_showingPlayer
                || Avalonia.Automation.AutomationProperties.GetName(button) != "Abrir player"))) return;

        _playerPointer = e.Pointer;
        _playerDragFromMini = !_showingPlayer;
        _playerPointerOrigin = _playerPointerLast = e.GetPosition(_root);
        _playerPointerTime = e.Timestamp;
        _playerVelocity = 0;
    }

    private void MovePlayerSwipe(object? sender, PointerEventArgs e)
    {
        if (_playerPointer != e.Pointer) return;
        var position = e.GetPosition(_root);
        var delta = position - _playerPointerOrigin;
        if (!_playerDragging)
        {
            if (Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)) < PlayerSwipeSlop) return;
            if (Math.Abs(delta.X) >= Math.Abs(delta.Y) || (_playerDragFromMini ? delta.Y >= 0 : delta.Y <= 0))
            {
                _playerPointer = null;
                return;
            }

            if (_playerDragFromMini) OpenPlayer(fromSwipe: true);
            if (_playerPage is null) { _playerPointer = null; return; }
            _playerDragStart = _playerPage.RenderTransform?.Value.M32 ?? 0;
            _playerDragging = true;
            e.Pointer.Capture(_root);
        }

        if (e.Timestamp > _playerPointerTime)
        {
            _playerVelocity = (position.Y - _playerPointerLast.Y) * 1000 / (e.Timestamp - _playerPointerTime);
            _playerPointerTime = e.Timestamp;
        }
        _playerPointerLast = position;
        if (_playerPage is { } player)
        {
            var offset = Math.Clamp(_playerDragStart + delta.Y, 0, PlayerCollapsedOffset());
            SetPlayerSlide(player, offset, animate: false);
        }
        e.Handled = true;
    }

    private void ReleasePlayerSwipe(object? sender, PointerReleasedEventArgs e)
    {
        if (_playerPointer != e.Pointer) return;
        if (!_playerDragging) { _playerPointer = null; return; }

        var distance = e.GetPosition(_root).Y - _playerPointerOrigin.Y;
        var velocity = e.Timestamp >= _playerPointerTime && e.Timestamp - _playerPointerTime < 95 ? _playerVelocity : 0;
        var threshold = Math.Clamp(PlayerCollapsedOffset() * .24, 65, 150);
        var direction = _playerDragFromMini ? -1 : 1;
        var commit = distance * direction >= threshold
            || distance * direction >= 24 && velocity * direction >= PlayerSwipeVelocity;
        FinishPlayerSwipe(e.Pointer, commit);
        e.Handled = true;
    }

    private void FinishPlayerSwipe(IPointer pointer, bool commit)
    {
        _playerPointer = null;
        _playerDragging = false;
        if (pointer.Captured == _root) pointer.Capture(null);
        if (_playerPage is not { } player) return;
        if (commit == _playerDragFromMini) SetPlayerSlide(player, 0, animate: true);
        else ClosePlayer();
    }

    private double PlayerCollapsedOffset() =>
        _mini.IsEffectivelyVisible && _mini.Bounds.Height > 0 && _mini.TranslatePoint(default, _root) is { Y: > 0 } origin
            ? origin.Y : Math.Max(0, _root.Bounds.Height);

    private static void SetPlayerSlide(Border player, double offset, bool animate)
    {
        if (!animate || UiMotion.Reduced) player.Transitions = null;
        else
        {
            var current = player.RenderTransform?.Value.M32 ?? 0;
            player.Transitions = null;
            player.RenderTransform = Translation(current);
            player.Transitions = new Transitions
            {
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = PlayerSlideDuration,
                    Easing = new CubicEaseOut()
                }
            };
        }
        player.RenderTransform = Translation(offset);

        static TransformOperations Translation(double y)
        {
            var transform = TransformOperations.CreateBuilder(1);
            transform.AppendTranslate(0, y);
            return transform.Build();
        }
    }

    private async void SlidePlayerOut(Border player)
    {
        var pointer = _playerPointer;
        _playerPointer = null;
        _playerDragging = false;
        if (pointer?.Captured == _root) pointer.Capture(null);
        _slidingOutPlayer = player;
        player.IsHitTestVisible = false;
        player.IsEnabled = false;
        // Slide past the mini card so it is fully visible before the overlay is removed.
        SetPlayerSlide(player, _root.Bounds.Height, animate: true);
        if (!UiMotion.Reduced) await Task.Delay(PlayerSlideDuration);
        _root.Children.Remove(player);
        if (ReferenceEquals(_slidingOutPlayer, player)) _slidingOutPlayer = null;
    }
}
