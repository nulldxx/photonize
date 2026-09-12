using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photonize.Helpers;
using Photonize.Services;

namespace Photonize.Views;

/// <summary>
/// Picks a range out of one clip, iPhone-trimmer style: a preview over a
/// filmstrip with a handle at each end.
///
/// Code-behind rather than MVVM, following <see cref="SliceVideoDialog"/> and
/// every other dialog in the app, with the result surfaced as properties that are
/// only meaningful once DialogResult is true.
/// </summary>
public partial class ExtractClipDialog : Window
{
    private readonly FilmstripGenerator _filmstrip = new();
    private readonly CancellationTokenSource _stripCancellation = new();

    private readonly string _filePath;
    private readonly TimeSpan _duration;

    private BitmapSource? _strip;
    private bool _previewUsable;
    private bool _playingRange;

    // MediaElement.Position seeks are expensive; driving one from every DragDelta
    // stalls the pipeline. Coalesce to the latest pending value instead.
    private readonly DispatcherTimer _seekTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private TimeSpan? _pendingSeek;

    // Drives the playhead and stops playback at the out-point.
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    /// <summary>The chosen range. Only meaningful when DialogResult is true.</summary>
    public TimeSpan ClipStart { get; private set; }

    public TimeSpan ClipEnd { get; private set; }

    /// <summary>True when the user asked for a frame-accurate, re-encoded cut.</summary>
    public bool Precise { get; private set; }

    public ExtractClipDialog(string filePath, string fileName, TimeSpan duration)
    {
        InitializeComponent();

        _filePath = filePath;
        _duration = duration;

        SubjectText.Text = $"{fileName} — {VideoOutputNaming.FormatDuration(duration)}";

        var (start, end) = DefaultRange(duration);
        Trim.Initialise(duration, start, end);
        Trim.RangeChanged += Trim_RangeChanged;
        Trim.ScrubRequested += Trim_ScrubRequested;
        Trim.DragFinished += Trim_DragFinished;

        _seekTimer.Tick += SeekTimer_Tick;
        _playbackTimer.Tick += PlaybackTimer_Tick;

        UpdateModeNote();
        UpdateRangeText();

        Loaded += ExtractClipDialog_Loaded;
    }

    /// <summary>
    /// Opens on a range the user can immediately drag either way, rather than one
    /// pinned to both ends of the clip with nowhere to go.
    /// </summary>
    private static (TimeSpan Start, TimeSpan End) DefaultRange(TimeSpan duration)
    {
        if (duration <= TrimGeometry.MinimumRange)
            return (TimeSpan.Zero, duration);

        if (duration > TimeSpan.FromSeconds(12))
        {
            // A tenth in from the front is past most clips' dead air, and ten
            // seconds is about what anyone wants out of a long clip.
            var start = TimeSpan.FromSeconds(duration.TotalSeconds * 0.1);
            return (start, start + TimeSpan.FromSeconds(10));
        }

        var margin = TimeSpan.FromSeconds(duration.TotalSeconds * 0.1);
        return (margin, duration - margin);
    }

    private async void ExtractClipDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // The player is opened here rather than in the constructor so that a
        // failure raises MediaFailed on a live window instead of throwing out of
        // the caller's command.
        Preview.Source = new Uri(_filePath, UriKind.Absolute);
        // With LoadedBehavior="Manual", MediaOpened does not fire until playback
        // is asked for -- the same trap MainWindow's player documents. Muted for
        // that one frame, or opening the dialog barks a fraction of a second of
        // audio before the pause lands.
        Preview.Volume = 0;
        Preview.Play();

        try
        {
            _strip = await _filmstrip.GenerateAsync(
                _filePath, _duration, FilmstripLayout.DefaultFrameCount, _stripCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // A filmstrip is a convenience. Losing it must not lose the dialog.
            _strip = null;
        }

        if (_stripCancellation.IsCancellationRequested)
            return;

        Trim.SetStrip(_strip);

        if (!_previewUsable)
            UpdateFallbackFrame(Trim.Start);
    }

    private void Preview_MediaOpened(object sender, RoutedEventArgs e)
    {
        _previewUsable = true;

        // ScrubbingEnabled only repaints while paused, so park it immediately and
        // let Position do the work from here.
        Preview.Pause();
        Preview.Position = Trim.Start;
        Preview.Volume = 0.5;
    }

    private void Preview_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // No MessageBox: the user asked to trim, not to play, and ffmpeg does not
        // care what Windows can decode. Extract stays available.
        _previewUsable = false;
        _playingRange = false;
        _playbackTimer.Stop();

        Preview.Visibility = Visibility.Collapsed;
        NoCodecPanel.Visibility = Visibility.Visible;
        PlayButton.IsEnabled = false;
        PlayHint.Text = "Playback needs a system codec this clip does not have";

        UpdateFallbackFrame(Trim.Start);
    }

    /// <summary>
    /// Crops the cell under <paramref name="time"/> out of the filmstrip, so the
    /// degraded mode still shows something of the clip rather than a grey box.
    /// </summary>
    private void UpdateFallbackFrame(TimeSpan time)
    {
        if (_strip == null || NoCodecPanel.Visibility != Visibility.Visible)
            return;

        var cells = FilmstripLayout.DefaultFrameCount;
        var index = FilmstripLayout.CellIndexAt(time, _duration, cells);
        var cellWidth = _strip.PixelWidth / cells;

        if (cellWidth <= 0)
            return;

        FallbackFrame.Source = new CroppedBitmap(
            _strip,
            new System.Windows.Int32Rect(index * cellWidth, 0, cellWidth, _strip.PixelHeight));
    }

    private void Trim_RangeChanged(object? sender, EventArgs e) => UpdateRangeText();

    private void Trim_ScrubRequested(object? sender, TimeSpan time)
    {
        StopRangePlayback();

        if (_previewUsable)
        {
            _pendingSeek = time;
            if (!_seekTimer.IsEnabled)
                _seekTimer.Start();
        }
        else
        {
            UpdateFallbackFrame(time);
        }
    }

    private void Trim_DragFinished(object? sender, TimeSpan time)
    {
        // Land exactly on the mark once the handle is released, rather than
        // wherever the last throttled seek happened to stop.
        _pendingSeek = null;
        _seekTimer.Stop();

        if (_previewUsable)
            Preview.Position = time;
        else
            UpdateFallbackFrame(time);
    }

    private void SeekTimer_Tick(object? sender, EventArgs e)
    {
        if (_pendingSeek is not { } time)
        {
            _seekTimer.Stop();
            return;
        }

        _pendingSeek = null;

        if (_previewUsable)
            Preview.Position = time;
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_previewUsable)
            return;

        if (_playingRange)
        {
            StopRangePlayback();
            return;
        }

        Preview.Position = Trim.Start;
        Preview.Play();
        _playingRange = true;
        PlayButton.Content = "⏸";
        _playbackTimer.Start();
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (!_playingRange)
            return;

        var position = Preview.Position;
        Trim.SetPlayhead(position);

        if (position >= Trim.End)
        {
            StopRangePlayback();
            Preview.Position = Trim.End;
        }
    }

    private void StopRangePlayback()
    {
        if (!_playingRange)
            return;

        _playingRange = false;
        _playbackTimer.Stop();
        PlayButton.Content = "▶";

        if (_previewUsable)
            Preview.Pause();
    }

    private void PreciseBox_Changed(object sender, RoutedEventArgs e) => UpdateModeNote();

    private void UpdateModeNote()
    {
        // Guard: Checked can fire during InitializeComponent, before ModeNote
        // exists -- the same trap SliceVideoDialog's TextChanged handler carries.
        if (ModeNote == null || PreciseBox == null)
            return;

        ModeNote.Text = PreciseBox.IsChecked == true
            ? "Re-encoded to H.264/AAC so the clip starts on the frame you chose. Slower, and an HDR clip is flattened to standard range."
            : "Nothing is re-encoded, so this is instant and keeps the original quality. A copied stream can only start on a keyframe, so the clip may begin a second or two before the mark.";
    }

    private void UpdateRangeText()
    {
        if (RangeText == null || ExtractButton == null)
            return;

        var valid = VideoOutputNaming.IsValidTrimRange(Trim.Start, Trim.End, _duration, out var error);

        ExtractButton.IsEnabled = valid;
        RangeText.Text = valid
            ? VideoOutputNaming.FormatTrimRange(Trim.Start, Trim.End)
            : error;
    }

    private void ExtractButton_Click(object sender, RoutedEventArgs e)
    {
        if (!VideoOutputNaming.IsValidTrimRange(Trim.Start, Trim.End, _duration, out _))
            return;

        ClipStart = Trim.Start;
        ClipEnd = Trim.End;
        Precise = PreciseBox.IsChecked == true;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _stripCancellation.Cancel();
        _seekTimer.Stop();
        _playbackTimer.Stop();

        // The player holds a read handle on the source. The folder reload that
        // follows a successful extract must not find it still open.
        Preview.Stop();
        Preview.Close();
        Preview.Source = null;

        _stripCancellation.Dispose();

        base.OnClosed(e);
    }
}
