using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace VoxInject.Infrastructure.Systray;

public sealed class SystrayController : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly ImageSource _normalSource;
    private readonly ImageSource _warningSource;
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

        // Build both icon variants once as BitmapImage (PNG stream — avoids InteropBitmap)
        using var normalBitmap  = LoadBaseBitmap();
        using var warningBitmap = BuildWarningBitmap(normalBitmap);
        _normalSource  = BitmapToImageSource(normalBitmap);
        _warningSource = BitmapToImageSource(warningBitmap);
    }

    // ── Warning badge ─────────────────────────────────────────────────────────

    /// <summary>
    /// Overlays (or removes) an orange dot on the systray icon.
    /// Must be called on the UI thread.
    /// </summary>
    public void SetWarning(bool warning)
    {
        if (_hasWarning == warning) return;
        _hasWarning = warning;
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

    private static Bitmap LoadBaseBitmap()
    {
        try
        {
            var stream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
            if (stream is not null)
            {
                using var icon = new Icon(stream, 16, 16);
                return icon.ToBitmap();
            }
        }
        catch { /* fall through */ }

        return new Bitmap(16, 16);
    }

    /// <summary>Draws a 6×6 px orange dot in the bottom-right corner of the base bitmap.</summary>
    private static Bitmap BuildWarningBitmap(Bitmap baseIcon)
    {
        var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        g.DrawImage(baseIcon, 0, 0, 16, 16);

        using var fill = new SolidBrush(System.Drawing.Color.FromArgb(255, 165, 0));
        g.FillEllipse(fill, 9, 9, 6, 6);

        using var ring = new System.Drawing.Pen(System.Drawing.Color.FromArgb(160, 100, 0), 1f);
        g.DrawEllipse(ring, 9, 9, 5, 5);

        return bmp;
    }

    /// <summary>
    /// Converts a GDI+ <see cref="Bitmap"/> to a frozen <see cref="BitmapImage"/> via PNG
    /// stream — the only <see cref="BitmapSource"/> subtype H.NotifyIcon accepts as
    /// <see cref="TaskbarIcon.IconSource"/>.
    /// </summary>
    private static ImageSource BitmapToImageSource(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource  = ms;
        bi.CacheOption   = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    public void Dispose() => _trayIcon.Dispose();
}
