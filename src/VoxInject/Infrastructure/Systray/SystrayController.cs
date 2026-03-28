using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace VoxInject.Infrastructure.Systray;

public sealed class SystrayController : IDisposable
{
    private readonly TaskbarIcon  _trayIcon;
    private readonly ImageSource  _normalSource;
    private readonly ImageSource  _warningSource;
    private bool                  _hasWarning;

    public SystrayController(Action openConfig, Action exitApp)
    {
        _trayIcon = (TaskbarIcon)Application.Current.FindResource("TrayIcon");

        var openItem = new MenuItem { Header = "Paramètres" };
        openItem.Click += (_, _) => openConfig();

        var exitItem = new MenuItem { Header = "Quitter" };
        exitItem.Click += (_, _) => exitApp();

        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu          = menu;
        _trayIcon.TrayMouseDoubleClick += (_, _) => openConfig();
        _trayIcon.ForceCreate();

        // Build both icon variants once; store as ImageSource so IconSource can be updated at runtime
        using var normalIcon  = LoadBaseIcon();
        using var warningIcon = BuildWarningIcon(normalIcon);
        _normalSource  = ToImageSource(normalIcon);
        _warningSource = ToImageSource(warningIcon);
    }

    // ── Warning badge ─────────────────────────────────────────────────────────

    /// <summary>
    /// Overlays (or removes) an orange dot on the systray icon to signal a
    /// configuration or session error without replacing the icon entirely.
    /// </summary>
    public void SetWarning(bool warning)
    {
        if (_hasWarning == warning) return;
        _hasWarning = warning;
        // Update IconSource (not Icon) — H.NotifyIcon uses IconSource as the authoritative source
        _trayIcon.IconSource = warning ? _warningSource : _normalSource;
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    public void Notify(string title, string message)
        => _trayIcon.ShowNotification(title, message, NotificationIcon.Info);

    public void NotifyWarning(string message)
        => _trayIcon.ShowNotification("VoxInject", message, NotificationIcon.Warning);

    public void NotifyError(string message)
        => _trayIcon.ShowNotification("VoxInject — Erreur", message, NotificationIcon.Error);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Icon LoadBaseIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
            if (stream is not null)
                return new Icon(stream, 16, 16);
        }
        catch { /* fall through */ }

        return SystemIcons.Application;
    }

    /// <summary>
    /// Draws the base icon with a 6 × 6 px orange dot in the bottom-right corner.
    /// </summary>
    private static Icon BuildWarningIcon(Icon baseIcon)
    {
        using var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g   = Graphics.FromImage(bmp);

        g.DrawIcon(baseIcon, 0, 0);

        // Orange dot — bottom-right corner, 6 × 6 px
        using var fill = new SolidBrush(System.Drawing.Color.FromArgb(255, 165, 0));
        g.FillEllipse(fill, 9, 9, 6, 6);

        // Thin dark ring so it pops on both light and dark taskbars
        using var ring = new System.Drawing.Pen(System.Drawing.Color.FromArgb(160, 100, 0), 1f);
        g.DrawEllipse(ring, 9, 9, 5, 5);

        var hIcon = bmp.GetHicon();
        var icon  = Icon.FromHandle(hIcon);
        return icon;
    }

    /// <summary>
    /// Converts a <see cref="Icon"/> to a frozen <see cref="BitmapSource"/> usable as
    /// <see cref="TaskbarIcon.IconSource"/> from any thread after freezing.
    /// </summary>
    private static ImageSource ToImageSource(Icon icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze(); // makes it cross-thread safe
        return source;
    }

    public void Dispose() => _trayIcon.Dispose();
}
