using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace VoxInject.Infrastructure.Systray;

public sealed class SystrayController : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    // Kept alive for the entire app lifetime — one 16×16 HICON, not a real leak.
    private readonly Icon        _warningIcon;
    private bool                 _hasWarning;

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

        _warningIcon = BuildWarningIcon();
    }

    // ── Warning badge ─────────────────────────────────────────────────────────

    /// <summary>
    /// Switches the systray icon between normal (IconSource from XAML) and
    /// warning (dynamically-drawn orange dot). Must be called on the UI thread.
    /// </summary>
    public void SetWarning(bool warning)
    {
        if (_hasWarning == warning) return;
        _hasWarning = warning;

        // Icon (System.Drawing.Icon) overrides IconSource when non-null.
        // Setting it back to null lets H.NotifyIcon fall through to IconSource.
        _trayIcon.Icon = warning ? _warningIcon : null;
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    public void Notify(string title, string message)
        => _trayIcon.ShowNotification(title, message, NotificationIcon.Info);

    public void NotifyWarning(string message)
        => _trayIcon.ShowNotification("VoxInject", message, NotificationIcon.Warning);

    public void NotifyError(string message)
        => _trayIcon.ShowNotification("VoxInject — Erreur", message, NotificationIcon.Error);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads app.ico at 16×16, then draws an orange 6×6 dot in the bottom-right corner.
    /// </summary>
    private static Icon BuildWarningIcon()
    {
        // Load base
        Bitmap? baseBitmap = null;
        try
        {
            var stream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
            if (stream is not null)
                using (var icon = new Icon(stream, 16, 16))
                    baseBitmap = icon.ToBitmap();
        }
        catch { /* fall through to blank */ }

        using var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g   = Graphics.FromImage(bmp);

        if (baseBitmap is not null)
        {
            g.DrawImage(baseBitmap, 0, 0, 16, 16);
            baseBitmap.Dispose();
        }

        // Orange dot — bottom-right corner
        using var fill = new SolidBrush(System.Drawing.Color.FromArgb(255, 165, 0));
        g.FillEllipse(fill, 9, 9, 6, 6);

        using var ring = new System.Drawing.Pen(System.Drawing.Color.FromArgb(160, 100, 0), 1f);
        g.DrawEllipse(ring, 9, 9, 5, 5);

        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose() => _trayIcon.Dispose();
}
