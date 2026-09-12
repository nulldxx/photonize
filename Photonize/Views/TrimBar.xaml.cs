using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Photonize.Helpers;

namespace Photonize.Views;

/// <summary>
/// The filmstrip and its two drag handles — the iPhone trimmer's shape.
///
/// The range is held as two <see cref="TimeSpan"/>s, never as pixels, so a resize
/// only has to re-derive x. All the arithmetic lives in
/// <see cref="TrimGeometry"/> so it can be unit-tested without a visual tree.
/// </summary>
public partial class TrimBar : UserControl
{
    private TimeSpan _duration;
    private TimeSpan _start;
    private TimeSpan _end;
    private TimeSpan _playhead;

    private bool _draggingStart;
    private double _grabOffset;

    /// <summary>Fires as either handle moves, for the read-out.</summary>
    public event EventHandler? RangeChanged;

    /// <summary>The time the preview should show. Fires continuously during a drag.</summary>
    public event EventHandler<TimeSpan>? ScrubRequested;

    /// <summary>Fires when a handle is let go, so the preview can land exactly.</summary>
    public event EventHandler<TimeSpan>? DragFinished;

    public TrimBar()
    {
        InitializeComponent();
        Loaded += (_, _) => Relayout();
    }

    public TimeSpan Start => _start;

    public TimeSpan End => _end;

    /// <summary>
    /// Points the bar at a clip. Called once, before the filmstrip arrives.
    /// </summary>
    public void Initialise(TimeSpan duration, TimeSpan start, TimeSpan end)
    {
        _duration = duration;
        (_start, _end) = TrimGeometry.ClampEnd(start, end, duration);
        _playhead = _start;
        Relayout();
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hands over the finished strip. Null leaves the placeholder showing — a
    /// missing filmstrip must not stop the clip being trimmed.
    /// </summary>
    public void SetStrip(BitmapSource? strip)
    {
        StripImage.Source = strip;
        StripStatus.Visibility = strip == null ? Visibility.Visible : Visibility.Collapsed;

        if (strip == null)
            StripStatus.Text = "No preview frames";
    }

    /// <summary>Moves the playhead without touching the range.</summary>
    public void SetPlayhead(TimeSpan time)
    {
        _playhead = time;
        Relayout();
    }

    private double TrackWidth => Math.Max(1.0, Track.ActualWidth);

    private void Track_SizeChanged(object sender, SizeChangedEventArgs e) => Relayout();

    private void Relayout()
    {
        // ActualWidth is 0 until the first layout pass. Positioning against it
        // then stacks every element at x=0, and nothing later moves them back.
        if (!IsLoaded || Track.ActualWidth <= 0 || Track.ActualHeight <= 0)
            return;

        var width = TrackWidth;
        var height = Track.ActualHeight;

        var startX = TrimGeometry.TimeToX(_start, _duration, width);
        var endX = TrimGeometry.TimeToX(_end, _duration, width);

        // A Rectangle in a Canvas has no size unless it is given one: without
        // these it simply does not render.
        SetRect(DimLeft, 0, startX, height);
        SetRect(DimRight, endX, width - endX, height);
        SetRect(RangeFrame, startX, endX - startX, height);

        // Negative at the left edge by design, so the grabber sits outside the
        // selection rather than covering the first frame.
        Canvas.SetLeft(StartHandle, startX - StartHandle.Width);
        Canvas.SetLeft(EndHandle, endX);
        StartHandle.Height = height;
        EndHandle.Height = height;

        var playheadX = TrimGeometry.TimeToX(_playhead, _duration, width);
        Playhead.X1 = playheadX;
        Playhead.X2 = playheadX;
        Playhead.Y2 = height;
        Playhead.Visibility = _playhead >= _start && _playhead <= _end
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void SetRect(Rectangle rectangle, double left, double width, double height)
    {
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, 0);
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = height;
    }

    private void Handle_DragStarted(object sender, DragStartedEventArgs e)
    {
        _draggingStart = ReferenceEquals(sender, StartHandle);

        // Where inside the grabber the user took hold, so the edge does not jump
        // to the cursor on the first pixel of movement.
        var edgeX = TrimGeometry.TimeToX(_draggingStart ? _start : _end, _duration, TrackWidth);
        _grabOffset = Mouse.GetPosition(Track).X - edgeX;
    }

    private void StartHandle_DragDelta(object sender, DragDeltaEventArgs e) => DragTo(isStart: true);

    private void EndHandle_DragDelta(object sender, DragDeltaEventArgs e) => DragTo(isStart: false);

    private void DragTo(bool isStart)
    {
        // Read the mouse absolutely rather than accumulating
        // DragDeltaEventArgs.HorizontalChange. That value is the change since the
        // last event, measured against a thumb that has already moved, so summing
        // it drifts the moment a clamp starts holding the handle still.
        var x = Mouse.GetPosition(Track).X - _grabOffset;
        var time = TrimGeometry.XToTime(x, _duration, TrackWidth);

        (_start, _end) = isStart
            ? TrimGeometry.ClampStart(time, _end, _duration)
            : TrimGeometry.ClampEnd(_start, time, _duration);

        _playhead = isStart ? _start : _end;

        Relayout();
        RangeChanged?.Invoke(this, EventArgs.Empty);
        ScrubRequested?.Invoke(this, _playhead);
    }

    private void Handle_DragCompleted(object sender, DragCompletedEventArgs e) =>
        DragFinished?.Invoke(this, _draggingStart ? _start : _end);

    /// <summary>
    /// Clicking anywhere on the track moves the playhead there, without disturbing
    /// the range — the way scrubbing a video works everywhere else.
    /// </summary>
    private void Track_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_duration <= TimeSpan.Zero)
            return;

        var time = TrimGeometry.XToTime(e.GetPosition(Track).X, _duration, TrackWidth);
        SetPlayhead(time);
        ScrubRequested?.Invoke(this, time);
        DragFinished?.Invoke(this, time);
    }
}
