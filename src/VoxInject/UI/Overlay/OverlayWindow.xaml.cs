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

    // Purple = initializing/processing, Orange = silence, Green = speaking
    private static readonly Color ColorInit     = Color.FromRgb(0x6A, 0x3D, 0xE8);
    private static readonly Color ColorSilence  = Color.FromRgb(0xFF, 0xA5, 0x00);
    private static readonly Color ColorSpeaking = Color.FromRgb(0x44, 0xCC, 0x44);

    private VoxState _currentState = VoxState.Idle;
    private bool     _isSpeaking;

    public OverlayWindow(ISettingsService settings)
    {
        _settings = settings;
        InitializeComponent();

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
        if (_isSpeaking == isSpeaking) return;
        _isSpeaking = isSpeaking;
        Dispatcher.InvokeAsync(UpdateDotColor);
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
        // Smooth interpolation toward target scale (ease-out feel)
        var target = Volatile.Read(ref _targetScale);
        _currentScale += (target - _currentScale) * 0.25;

        PulseScale.ScaleX = _currentScale;
        PulseScale.ScaleY = _currentScale;

        // Show pulse ring only when actively pulsing above resting scale
        PulseRing.Opacity = Math.Max(0, (_currentScale - 1.0) * 0.8);
    }

    private void ApplyStateVisuals(VoxState state)
    {
        _currentState = state;
        _isSpeaking   = false;
        UpdateDotColor();

        var size = _settings.Current.OverlaySize;
        Width   = size;
        Height  = size;
        Opacity = _settings.Current.OverlayOpacity;
    }

    private void UpdateDotColor()
    {
        var color = (_currentState == VoxState.Listening && _isSpeaking)
            ? ColorSpeaking
            : (_currentState == VoxState.Listening)
                ? ColorSilence
                : ColorInit;

        StatusDot.Fill   = new SolidColorBrush(color);
        PulseRing.Stroke = new SolidColorBrush(Color.FromArgb(0xAA, color.R, color.G, color.B));
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

        (Left, Top) = s.OverlayCorner switch
        {
            OverlayCorner.TopLeft     => (left  + margin,         top    + margin),
            OverlayCorner.TopRight    => (right - size - margin,  top    + margin),
            OverlayCorner.BottomLeft  => (left  + margin,         bottom - size - margin),
            OverlayCorner.BottomRight => (right - size - margin,  bottom - size - margin),
            _                         => (right - size - margin,  bottom - size - margin)
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
