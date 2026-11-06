using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Tapedeck.Controls;

/// <summary>
/// Lets a <see cref="ScrollViewer"/> glide to where a mouse wheel notch points instead of jumping there.
/// Only whole notches are animated; touchpads already scroll in fine steps and keep Avalonia's behavior.
/// Set for every ScrollViewer by a global style in <c>TapedeckStyles.axaml</c>.
/// </summary>
public sealed class SmoothScroll : AvaloniaObject
{
    // a notch moves a little more than Avalonia's 50 px, since the glide makes it feel shorter
    private const double NOTCH_PIXELS = 80;

    // how quickly the offset follows the target: about 95 % of the way after three time constants
    private static readonly TimeSpan TIME_CONSTANT = TimeSpan.FromMilliseconds(55);

    private static readonly ConditionalWeakTable<ScrollViewer, Glide> GLIDES = [];

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<SmoothScroll, ScrollViewer, bool>("IsEnabled");

    static SmoothScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, e) =>
        {
            // tunnel: the presenter inside would otherwise handle the wheel first and jump
            viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
            if (e.NewValue is true)
                viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        });
    }

    public static bool GetIsEnabled(ScrollViewer element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        // Shift and Ctrl mean sideways or zoom; fractions come from touchpads
        if (sender is not ScrollViewer viewer || e.Handled || e.KeyModifiers != KeyModifiers.None
            || Math.Abs(e.Delta.Y) < 1 || e.Delta.X != 0)
            return;

        var down = e.Delta.Y < 0;
        // a list or text box inside that can still scroll this way gets the wheel itself
        if (InnerCanScroll(viewer, e.Source as Visual, down) || !CanScroll(viewer, down))
            return;

        var glide = GLIDES.GetValue(viewer, _ => new Glide());
        if (!glide.Running)
            glide.Target = viewer.Offset.Y;
        glide.Target = Math.Clamp(glide.Target - e.Delta.Y * NOTCH_PIXELS, 0, viewer.ScrollBarMaximum.Y);
        e.Handled = true;

        if (glide.Running || TopLevel.GetTopLevel(viewer) is not { } top)
            return;
        glide.Running = true;
        glide.LastSet = viewer.Offset.Y;
        glide.LastFrame = null;
        top.RequestAnimationFrame(time => Step(viewer, top, glide, time));
    }

    private static void Step(ScrollViewer viewer, TopLevel top, Glide glide, TimeSpan time)
    {
        var current = viewer.Offset.Y;
        // someone else moved it, e.g. the scroll bar or scrolling to the selected track: let them
        if (Math.Abs(current - glide.LastSet) > 1)
        {
            glide.Running = false;
            return;
        }

        // the virtualized list estimates its height anew while scrolling
        var target = Math.Clamp(glide.Target, 0, viewer.ScrollBarMaximum.Y);
        var elapsed = glide.LastFrame is { } last ? (time - last).TotalSeconds : 1.0 / 60;
        glide.LastFrame = time;
        var next = current + (target - current) * (1 - Math.Exp(-elapsed / TIME_CONSTANT.TotalSeconds));
        var done = Math.Abs(target - next) < 0.5;
        if (done)
            next = target;

        viewer.Offset = new Vector(viewer.Offset.X, next);
        glide.LastSet = next;
        if (done)
            glide.Running = false;
        else
            top.RequestAnimationFrame(t => Step(viewer, top, glide, t));
    }

    private static bool InnerCanScroll(ScrollViewer viewer, Visual? source, bool down)
    {
        for (var visual = source; visual is not null && visual != viewer; visual = visual.GetVisualParent())
        {
            if (visual is ScrollViewer inner && CanScroll(inner, down))
                return true;
        }
        return false;
    }

    private static bool CanScroll(ScrollViewer viewer, bool down) =>
        down ? viewer.Offset.Y < viewer.ScrollBarMaximum.Y - 0.5 : viewer.Offset.Y > 0.5;

    private sealed class Glide
    {
        public double Target { get; set; }

        public double LastSet { get; set; }

        public TimeSpan? LastFrame { get; set; }

        public bool Running { get; set; }
    }
}
