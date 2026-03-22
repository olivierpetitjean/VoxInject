using System.Windows;
using VoxInject.Core.Services;
using VoxInject.Core.State;
using VoxInject.Infrastructure;
using VoxInject.Infrastructure.Systray;
using VoxInject.Infrastructure.Win32;
using VoxInject.Providers.Abstractions;
using VoxInject.UI.Config;
using VoxInject.UI.Overlay;

namespace VoxInject;

public partial class App : Application
{
    private SettingsService?                    _settings;
    private DpapiSecretStore?                   _secrets;
    private SystrayController?                  _systray;
    private HotkeyManager?                      _hotkey;
    private VoxController?                      _vox;
    private OverlayWindow?                      _overlay;
    private ConfigWindow?                       _configWindow;
    private IReadOnlyList<ITranscriptionProvider> _providers = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        _providers = PluginLoader.Load(pluginsDir);

        _settings = new SettingsService();
        _secrets  = new DpapiSecretStore();
        _overlay  = new OverlayWindow(_settings);
        _vox      = new VoxController(_settings, _secrets, _overlay, _providers);
        _hotkey   = new HotkeyManager();
        _systray  = new SystrayController(OpenConfigWindow, () => Shutdown());

        _hotkey.HotkeyPressed  += OnHotkeyPressed;
        _hotkey.HotkeyReleased += OnHotkeyReleased;
        _vox.Error             += msg =>
        {
            _systray?.NotifyError(msg);
            _systray?.SetWarning(true);
        };
        _vox.SessionStarted += () => _systray?.SetWarning(false);

        TryRegisterHotkey(_settings.Current.HotkeyModifiers, _settings.Current.HotkeyVk);

        _settings.SettingsChanged += (_, s) =>
            TryRegisterHotkey(s.HotkeyModifiers, s.HotkeyVk);

        // Open settings if no provider is configured at all
        var activeProvider = _providers.FirstOrDefault(
            p => p.Id == _settings.Current.ActiveProviderId) ?? _providers.FirstOrDefault();
        var hasAnySecret = activeProvider?.ConfigFields
            .Where(f => f.Type == ProviderFieldType.Password)
            .Any(f => !string.IsNullOrEmpty(_secrets.Load($"{activeProvider.Id}-{f.Key}"))) ?? false;
        if (!hasAnySecret)
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
        _configWindow = new ConfigWindow(_settings!, _secrets!, _providers);
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
