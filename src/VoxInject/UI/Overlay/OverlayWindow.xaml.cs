using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VoxInject.Core.Models;
using VoxInject.Core.State;
using VoxInject.Infrastructure.Win32;
using ISettingsService = VoxInject.Core.Services.ISettingsService;

namespace VoxInject.UI.Overlay;

public partial class OverlayWindow : Window
{
    private readonly ISettingsService _settings;

    // Audio level animation: a timer samples the latest level and smoothly
    // drives the PulseScale transform. No per-buffer Storyboard objects.
    private readonly DispatcherTimer _levelTimer;
    private double _targetScale = 1.0;
    private double _currentScale = 1.0;

    // Purple = init/processing, Orange = listening/silent, Green base/light = speaking
    private static readonly Color ColorInit          = Color.FromRgb(0x6A, 0x3D, 0xE8);
    private static readonly Color ColorSilence       = Color.FromRgb(0xFF, 0xA5, 0x00);
    private static readonly Color ColorSpeakingBase  = Color.FromRgb(0x44, 0xCC, 0x44);
    private static readonly Color ColorSpeakingLight = Color.FromRgb(0x88, 0xFF, 0x88);

    // Reusable brushes — updated in-place to avoid GC pressure at 30 fps
    private readonly SolidColorBrush _dotBrush  = new(Color.FromRgb(0xFF, 0xA5, 0x00));
    private readonly SolidColorBrush _ringBrush = new(Color.FromArgb(0, 0xFF, 0xA5, 0x00));

    private VoxState _currentState    = VoxState.Idle;
    private bool     _isSpeaking;

    // Speaking-hold: stay green for ~10 frames (~330 ms) after audio drops below
    // threshold, so brief inter-word silences don't flash orange.
    private volatile int _speakingHoldFrames;
    private const    int SpeakingHoldFrames = 10;

    public OverlayWindow(ISettingsService settings)
    {
        _settings = settings;
        InitializeComponent();

        // Wire reusable brushes to XAML elements
        StatusDot.Fill   = _dotBrush;
        PulseRing.Stroke = _ringBrush;

        _levelTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30 fps
        };
        _levelTimer.Tick += OnLevelTick;
    }

    // ── Win32 extended style: must be applied after HWND exists ──────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd    = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    // ── Public API called by VoxController ───────────────────────────────────

    public void ShowForState(VoxState state)
    {
        ApplyStateVisuals(state);

        // Remove any animation clock that is holding Root.Opacity at 1
        // (FillBehavior.HoldEnd default), then reset the local value so the
        // FadeIn storyboard always starts cleanly from 0.
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        Root.Opacity = 0;

        // Show first so the HWND exists before PositionToCorner calls
        // PointFromScreen for DPI conversion.
        if (!IsVisible) Show();

        PositionToCorner();

        var fadeIn = (Storyboard)Resources["FadeIn"];
        fadeIn.Begin(this);

        _levelTimer.Start();
    }

    public new void Hide()
    {
        _levelTimer.Stop();
        // Remove any held animation before setting the local value, otherwise
        // the HoldEnd clock overrides the local Opacity = 0 assignment.
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        Root.Opacity = 0;
        base.Hide();
    }

    public void UpdateState(VoxState state)
        => Dispatcher.Invoke(() => ApplyStateVisuals(state));

    /// <summary>
    /// Called by VoxController when the audio level crosses the silence threshold.
    /// Thread-safe: can be called from audio threads.
    /// </summary>
    public void SetSpeaking(bool isSpeaking)
    {
        if (isSpeaking)
            Interlocked.Exchange(ref _speakingHoldFrames, SpeakingHoldFrames);
        // Silence: let the hold countdown in OnLevelTick expire naturally
    }

    /// <summary>
    /// Called by AudioCaptureService on each buffer with the current RMS dB level.
    /// Thread-safe: can be called from audio threads.
    /// </summary>
    public void SetAudioLevel(double db)
    {
        // Map -60 dB → 0 dB to scale 1.0 → 1.5
        const double minDb = -60.0;
        const double maxDb = 0.0;
        var normalized = Math.Clamp((db - minDb) / (maxDb - minDb), 0.0, 1.0);
        Interlocked.Exchange(ref _targetScale, 1.0 + normalized * 0.5);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void OnLevelTick(object? sender, EventArgs e)
    {
        // ── Speaking-hold countdown ───────────────────────────────────────────
        var prevSpeaking = _isSpeaking;
        if (_speakingHoldFrames > 0)
        {
            Interlocked.Decrement(ref _speakingHoldFrames);
            _isSpeaking = true;
        }
        else
        {
            _isSpeaking = false;
        }
        if (_isSpeaking != prevSpeaking)
            UpdateDotColor();

        // ── Scale animation (ease-out toward target) ─────────────────────────
        var target = Volatile.Read(ref _targetScale);
        _currentScale += (target - _currentScale) * 0.25;

        PulseScale.ScaleX = _currentScale;
        PulseScale.ScaleY = _currentScale;

        // ── Color fluctuation when speaking ──────────────────────────────────
        // Interpolate dot and ring between base green and a slightly lighter
        // shade proportional to audio level — no orange flashing, subtle only.
        if (_currentState == VoxState.Listening && _isSpeaking)
        {
            var t = Math.Clamp((_currentScale - 1.0) / 0.5, 0.0, 1.0);
            var c = LerpColor(ColorSpeakingBase, ColorSpeakingLight, t);
            _dotBrush.Color  = c;
            _ringBrush.Color = Color.FromArgb((byte)(0x99 * t), c.R, c.G, c.B);
            PulseRing.Opacity = 1;
        }
        else
        {
            PulseRing.Opacity = 0;
        }
    }

    private static Color LerpColor(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    // Transparent padding around the circle so the pulse ring has room to scale.
    private const int RingPad = 20;

    private void ApplyStateVisuals(VoxState state)
    {
        _currentState = state;
        _isSpeaking   = false;
        UpdateDotColor();

        var size = _settings.Current.OverlaySize;
        Width   = size + RingPad * 2;
        Height  = size + RingPad * 2;
        Opacity = _settings.Current.OverlayOpacity;

        ContentGrid.Width  = size;
        ContentGrid.Height = size;
        PulseRing.Width    = size;
        PulseRing.Height   = size;
        PulseScale.CenterX = size / 2.0;
        PulseScale.CenterY = size / 2.0;
    }

    private void UpdateDotColor()
    {
        var color = (_currentState == VoxState.Listening && _isSpeaking)
            ? ColorSpeakingBase
            : (_currentState == VoxState.Listening)
                ? ColorSilence
                : ColorInit;

        _dotBrush.Color  = color;
        _ringBrush.Color = Color.FromArgb(0, color.R, color.G, color.B);
    }

    private void PositionToCorner()
    {
        var s            = _settings.Current;
        var size         = s.OverlaySize;
        const int margin = 24;

        var screen   = s.OverlayScreen == OverlayScreen.ActiveScreen
                           ? GetActiveScreen()
                           : System.Windows.Forms.Screen.PrimaryScreen!;

        // Screen.WorkingArea is in physical pixels. Convert to WPF DIPs using
        // GetDpiForMonitor so the result is correct regardless of where the
        // window happens to be sitting when PositionToCorner is called.
        var raw      = screen.WorkingArea;
        var dpi      = GetDpiScale(screen);

        double left   = raw.Left   / dpi;
        double top    = raw.Top    / dpi;
        double right  = raw.Right  / dpi;
        double bottom = raw.Bottom / dpi;

        // The window is (size + 2×RingPad) wide/tall. Offset by RingPad so the
        // visible circle lands at the intended corner position.
        (Left, Top) = s.OverlayCorner switch
        {
            OverlayCorner.TopLeft     => (left  + margin - RingPad,          top    + margin - RingPad),
            OverlayCorner.TopRight    => (right - size - margin - RingPad,   top    + margin - RingPad),
            OverlayCorner.BottomLeft  => (left  + margin - RingPad,          bottom - size - margin - RingPad),
            OverlayCorner.BottomRight => (right - size - margin - RingPad,   bottom - size - margin - RingPad),
            _                         => (right - size - margin - RingPad,   bottom - size - margin - RingPad)
        };
    }

    private static double GetDpiScale(System.Windows.Forms.Screen screen)
    {
        var pt      = new System.Drawing.Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
        var monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out _);
        return dpiX == 0 ? 1.0 : dpiX / 96.0;
    }

    private static System.Windows.Forms.Screen GetActiveScreen()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        return System.Windows.Forms.Screen.FromPoint(cursor)
               ?? System.Windows.Forms.Screen.PrimaryScreen!;
    }
}
