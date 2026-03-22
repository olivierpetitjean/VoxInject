using System.Windows;
using VoxInject.Core.Services;
using VoxInject.Core.State;
using VoxInject.Infrastructure.Systray;
using VoxInject.Infrastructure.Win32;
using VoxInject.UI.Config;
using VoxInject.UI.Overlay;

namespace VoxInject;

public partial class App : Application
{
    private SettingsService?   _settings;
    private DpapiSecretStore?  _secrets;
    private SystrayController? _systray;
    private HotkeyManager?     _hotkey;
    private VoxController?     _vox;
    private OverlayWindow?     _overlay;
    private ConfigWindow?      _configWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = new SettingsService();
        _secrets  = new DpapiSecretStore();
        _overlay  = new OverlayWindow(_settings);
        _vox      = new VoxController(_settings, _secrets, _overlay);
        _hotkey   = new HotkeyManager();
        _systray  = new SystrayController(OpenConfigWindow, () => Shutdown());

        _hotkey.HotkeyPressed  += OnHotkeyPressed;
        _hotkey.HotkeyReleased += OnHotkeyReleased;
        _vox.Error             += msg => _systray?.NotifyError(msg);

        TryRegisterHotkey(_settings.Current.HotkeyModifiers, _settings.Current.HotkeyVk);

        _settings.SettingsChanged += (_, s) =>
            TryRegisterHotkey(s.HotkeyModifiers, s.HotkeyVk);

        if (string.IsNullOrEmpty(_secrets.Load("assemblyai-apikey")))
            OpenConfigWindow();
    }

    private void TryRegisterHotkey(uint mods, uint vk)
    {
        try
        {
            _hotkey!.Register(mods, vk);
        }
        catch (Exception ex)
        {
            _systray?.NotifyError($"Cannot register hotkey: {ex.Message}\nChange it in Settings.");
        }
    }

    private async void OnHotkeyPressed()
    {
        try
        {
            await _vox!.OnHotkeyPressedAsync();
        }
        catch (Exception ex)
        {
            _systray?.NotifyError($"Recording error: {ex.Message}");
        }
    }

    private async void OnHotkeyReleased()
    {
        try
        {
            await _vox!.OnHotkeyReleasedAsync();
        }
        catch (Exception ex)
        {
            _systray?.NotifyError($"Recording error: {ex.Message}");
        }
    }

    private void OpenConfigWindow()
    {
        if (_configWindow is { IsVisible: true })
        {
            _configWindow.Activate();
            return;
        }
        _configWindow = new ConfigWindow(_settings!, _secrets!);
        _configWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _vox?.Dispose();
        _overlay?.Close();
        _systray?.Dispose();
        base.OnExit(e);
    }
}
