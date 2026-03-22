using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace VoxInject.Infrastructure.Systray;

public sealed class SystrayController : IDisposable
{
    private readonly TaskbarIcon _trayIcon;

    public SystrayController(Action openConfig, Action exitApp)
    {
        _trayIcon = (TaskbarIcon)Application.Current.FindResource("TrayIcon");

        var openItem = new MenuItem { Header = "Settings" };
        openItem.Click += (_, _) => openConfig();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => exitApp();

        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu          = menu;
        _trayIcon.TrayMouseDoubleClick += (_, _) => openConfig();
        _trayIcon.ForceCreate();
    }

    public void Notify(string title, string message)
    {
        _trayIcon.ShowNotification(title, message, NotificationIcon.Info);
    }

    public void NotifyError(string message)
    {
        _trayIcon.ShowNotification("VoxInject — Error", message, NotificationIcon.Error);
    }

    public void Dispose() => _trayIcon.Dispose();
}
