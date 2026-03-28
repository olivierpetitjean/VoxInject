using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NAudio.Wave;
using VoxInject.Core.Models;
using VoxInject.Core.Services;
using VoxInject.Providers.Abstractions;
using Wpf.Ui.Controls;

namespace VoxInject.UI.Config;

public partial class ConfigWindow : FluentWindow
{
    private readonly ISettingsService                      _settingsService;
    private readonly ISecretStore                          _secrets;
    private readonly IReadOnlyList<ITranscriptionProvider> _providers;

    private AppSettings   _settings;
    private List<Profile> _profiles;
    private Profile?      _activeProfile;

    // Mic test state
    private AudioCaptureService? _testAudio;

    // Hotkey capture state
    private uint _capturedModifiers;
    private uint _capturedVk;

    public ConfigWindow(
        ISettingsService                      settingsService,
        ISecretStore                          secrets,
        IReadOnlyList<ITranscriptionProvider> providers)
    {
        _settingsService = settingsService;
        _secrets         = secrets;
        _providers       = providers;
        _settings        = settingsService.Current;
        _profiles        = [.._settings.Profiles];

        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => StopMicTest();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateProviders();
        PopulateMicrophones();
        PopulateProfiles();
        LoadGlobalSettings();
    }

    // ── Provider / API ───────────────────────────────────────────────────────

    private void PopulateProviders()
    {
        ProviderCombo.Items.Clear();
        foreach (var p in _providers)
            ProviderCombo.Items.Add(new ComboBoxItem { Content = p.DisplayName, Tag = p.Id });

        // Select active provider
        SelectComboByTag(ProviderCombo, _settings.ActiveProviderId);
        if (ProviderCombo.SelectedIndex < 0 && ProviderCombo.Items.Count > 0)
            ProviderCombo.SelectedIndex = 0;

        RenderProviderFields();
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RenderProviderFields();

    private void RenderProviderFields()
    {
        ProviderFieldsPanel.Children.Clear();
        var providerId = (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var provider   = _providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null) return;

        foreach (var field in provider.ConfigFields)
        {
            var label = new System.Windows.Controls.TextBlock
            {
                Text       = field.Label,
                FontSize   = 12,
                Opacity    = 0.7,
                Margin     = new Thickness(0, 0, 0, 4)
            };
            ProviderFieldsPanel.Children.Add(label);

            if (field.Type == ProviderFieldType.Password)
            {
                var secret = _secrets.Load($"{provider.Id}-{field.Key}") ?? string.Empty;
                var box = new Wpf.Ui.Controls.PasswordBox
                {
                    PlaceholderText = field.Placeholder,
                    Password        = secret,
                    Margin          = new Thickness(0, 0, 0, 16),
                    Tag             = field.Key
                };
                ProviderFieldsPanel.Children.Add(box);
            }
            else
            {
                var textConfigs = _settings.ProviderTextConfigs;
                var existing    = textConfigs.TryGetValue(provider.Id, out var fd)
                                  && fd.TryGetValue(field.Key, out var v) ? v : string.Empty;
                var box = new System.Windows.Controls.TextBox
                {
                    Text       = existing,
                    Margin     = new Thickness(0, 0, 0, 16),
                    Tag        = field.Key
                };
                ProviderFieldsPanel.Children.Add(box);
            }
        }
    }

    private void SaveProviderFields()
    {
        var providerId = (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var provider   = _providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null) return;

        var textFields = new Dictionary<string, string>();

        foreach (var field in provider.ConfigFields)
        {
            // Find matching control by Tag
            var control = ProviderFieldsPanel.Children
                .OfType<FrameworkElement>()
                .FirstOrDefault(c => c.Tag?.ToString() == field.Key);

            if (field.Type == ProviderFieldType.Password
                && control is Wpf.Ui.Controls.PasswordBox pb)
            {
                if (!string.IsNullOrEmpty(pb.Password))
                    _secrets.Save($"{provider.Id}-{field.Key}", pb.Password);
            }
            else if (control is System.Windows.Controls.TextBox tb)
            {
                textFields[field.Key] = tb.Text;
            }
        }

        // Merge back into settings (done in Save_Click)
        _pendingProviderTextFields = (providerId!, textFields);
    }

    // Temporary holder filled by SaveProviderFields(), applied in Save_Click
    private (string ProviderId, Dictionary<string, string> Fields)? _pendingProviderTextFields;

    // ── Microphone ───────────────────────────────────────────────────────────

    private void PopulateMicrophones()
    {
        MicCombo.Items.Clear();
        MicCombo.Items.Add(new ComboBoxItem { Content = "Default", Tag = string.Empty });

        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var cap = WaveIn.GetCapabilities(i);
            MicCombo.Items.Add(new ComboBoxItem
            {
                Content = cap.ProductName,
                Tag     = i.ToString()
            });
        }

        var deviceId = _activeProfile?.MicrophoneDeviceId ?? string.Empty;
        SelectComboByTag(MicCombo, deviceId);
    }

    private void TestMic_Click(object sender, RoutedEventArgs e)
    {
        if (_testAudio is null) StartMicTest();
        else                    StopMicTest();
    }

    private void StartMicTest()
    {
        var deviceId = (MicCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        _testAudio = new AudioCaptureService();
        _testAudio.LevelChanged  += OnTestMicLevel;
        _testAudio.CaptureFailed += OnTestMicFailed;
        // Silence threshold at minimum — we only need the level signal, not silence detection
        _testAudio.Start(deviceId, silenceThresholdDb: -80.0, silenceTimeoutMs: int.MaxValue);

        MicLevelBar.Visibility = Visibility.Visible;
        TestMicButton.Content  = "Arrêter";
    }

    private void StopMicTest()
    {
        if (_testAudio is null) return;
        _testAudio.LevelChanged  -= OnTestMicLevel;
        _testAudio.CaptureFailed -= OnTestMicFailed;
        _testAudio.Stop();
        _testAudio.Dispose();
        _testAudio = null;

        MicLevelBar.Visibility = Visibility.Collapsed;
        MicLevelBar.Value      = 0;
        TestMicButton.Content  = "Test";
    }

    private void OnTestMicLevel(double db)
    {
        // Map -60 dB → 0 dB to 0 → 100 %
        var value = Math.Clamp((db + 60.0) / 60.0 * 100.0, 0.0, 100.0);
        Dispatcher.BeginInvoke(() => MicLevelBar.Value = value);
    }

    private void OnTestMicFailed(string _) => Dispatcher.BeginInvoke(StopMicTest);

    // ── Profiles ─────────────────────────────────────────────────────────────

    private void PopulateProfiles()
    {
        ProfileCombo.Items.Clear();
        foreach (var p in _profiles)
            ProfileCombo.Items.Add(p.Name);

        var activeName = _settings.ActiveProfileName;
        var idx = _profiles.FindIndex(p => p.Name == activeName);
        ProfileCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileCombo.SelectedIndex < 0) return;
        _activeProfile = _profiles[ProfileCombo.SelectedIndex];
        LoadProfileFields(_activeProfile);
    }

    private void LoadProfileFields(Profile p)
    {
        LanguageBox.Text           = p.Language;
        VocabBoostBox.Text         = string.Join(", ", p.VocabularyBoost);
        AutoPunctCheck.IsChecked   = p.AutoPunctuation;

        SelectComboByTag(RecordModeCombo, p.Mode.ToString());
        SelectComboByTag(MicCombo, p.MicrophoneDeviceId);

        AutoEnterCheck.IsChecked = p.AutoEnterOnSilence;
        SilenceBox.Text          = p.SilenceTimeoutMs.ToString();
        SelectComboByTag(EnterKeyCombo, p.UseShiftEnter ? "ShiftEnter" : "Enter");
        SilencePanel.IsEnabled   = p.AutoEnterOnSilence;
        SilencePanel.Opacity     = p.AutoEnterOnSilence ? 1.0 : 0.4;
    }

    private Profile ReadProfileFields(string name) => new Profile
    {
        Name               = name,
        Language           = LanguageBox.Text.Trim(),
        VocabularyBoost    = VocabBoostBox.Text
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        AutoPunctuation    = AutoPunctCheck.IsChecked == true,
        Mode               = Enum.TryParse<RecordingMode>(
                                 (RecordModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                                 out var mode) ? mode : RecordingMode.Toggle,
        MicrophoneDeviceId = (MicCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty,
        AutoEnterOnSilence = AutoEnterCheck.IsChecked == true,
        UseShiftEnter      = (EnterKeyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "ShiftEnter",
        SilenceTimeoutMs   = int.TryParse(SilenceBox.Text, out var ms) ? ms : 1500
    };

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileNameDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var name = dialog.ProfileName;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_profiles.Any(p => p.Name == name))
        {
            MessageBox.Show("A profile with that name already exists.");
            return;
        }
        _profiles.Add(new Profile { Name = name });
        ProfileCombo.Items.Add(name);
        ProfileCombo.SelectedIndex = _profiles.Count - 1;
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count <= 1)
        {
            MessageBox.Show("You need at least one profile.");
            return;
        }
        var idx = ProfileCombo.SelectedIndex;
        _profiles.RemoveAt(idx);
        ProfileCombo.Items.RemoveAt(idx);
        ProfileCombo.SelectedIndex = 0;
    }

    private void AutoEnterCheck_Changed(object sender, RoutedEventArgs e)
    {
        var enabled            = AutoEnterCheck.IsChecked == true;
        SilencePanel.IsEnabled = enabled;
        SilencePanel.Opacity   = enabled ? 1.0 : 0.4;
    }

    // ── Global settings ───────────────────────────────────────────────────────

    private void LoadGlobalSettings()
    {
        SelectComboByTag(CornerCombo,  _settings.OverlayCorner.ToString());
        SelectComboByTag(ScreenCombo,  _settings.OverlayScreen.ToString());
        OpacitySlider.Value      = _settings.OverlayOpacity;
        SizeSlider.Value         = _settings.OverlaySize;
        ToneCheck.IsChecked      = _settings.ToneEnabled;
        ToneVolumeSlider.Value   = _settings.ToneVolume;
        AutoStartCheck.IsChecked = _settings.AutoStartWithWindows;

        // Hotkey display
        _capturedModifiers = _settings.HotkeyModifiers;
        _capturedVk        = _settings.HotkeyVk;
        HotkeyBox.Text     = FormatHotkey(_capturedModifiers, _capturedVk);
    }

    // ── Hotkey capture ────────────────────────────────────────────────────────

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
        => HotkeyBox.Text = "Press your shortcut…";

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
        => HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedVk);

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore modifier-only keypresses
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        uint mods = 0;
        if (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl))  mods |= 0x0002;
        if (Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt))   mods |= 0x0001;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= 0x0004;

        _capturedModifiers = mods;
        _capturedVk        = (uint)KeyInterop.VirtualKeyFromKey(key);
        HotkeyBox.Text     = FormatHotkey(_capturedModifiers, _capturedVk);
    }

    private static string FormatHotkey(uint mods, uint vk)
    {
        var parts = new List<string>();
        if ((mods & 0x0002) != 0) parts.Add("Ctrl");
        if ((mods & 0x0001) != 0) parts.Add("Alt");
        if ((mods & 0x0004) != 0) parts.Add("Shift");
        parts.Add(((System.Windows.Forms.Keys)vk).ToString());
        return string.Join("+", parts);
    }

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveProviderFields();

        // Save active profile edits back to list
        if (_activeProfile != null)
        {
            var idx = _profiles.FindIndex(p => p.Name == _activeProfile.Name);
            if (idx >= 0)
                _profiles[idx] = ReadProfileFields(_activeProfile.Name);
        }

        var newSettings = _settings with
        {
            ActiveProfileName = (string)ProfileCombo.SelectedItem,
            Profiles          = _profiles,
            HotkeyModifiers   = _capturedModifiers,
            HotkeyVk          = _capturedVk,
            OverlayCorner     = Enum.Parse<OverlayCorner>(
                                    (CornerCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "BottomRight"),
            OverlayScreen     = Enum.Parse<OverlayScreen>(
                                    (ScreenCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ActiveScreen"),
            OverlayOpacity    = OpacitySlider.Value,
            OverlaySize       = (int)SizeSlider.Value,
            ToneEnabled          = ToneCheck.IsChecked == true,
            ToneVolume           = ToneVolumeSlider.Value,
            AutoStartWithWindows = AutoStartCheck.IsChecked == true,
            ActiveProviderId     = (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                                   ?? _settings.ActiveProviderId,
            ProviderTextConfigs  = BuildProviderTextConfigs()
        };

        _settingsService.Save(newSettings);
        ApplyAutoStart(newSettings.AutoStartWithWindows);
        Close();
    }

    private Dictionary<string, Dictionary<string, string>> BuildProviderTextConfigs()
    {
        // Start from existing config and overlay the pending save
        var result = new Dictionary<string, Dictionary<string, string>>(
            _settings.ProviderTextConfigs.Select(kv =>
                KeyValuePair.Create(kv.Key, new Dictionary<string, string>(kv.Value))));

        if (_pendingProviderTextFields.HasValue)
        {
            var (pid, fields) = _pendingProviderTextFields.Value;
            if (!result.TryGetValue(pid, out var existing))
                result[pid] = existing = [];
            foreach (var (k, v) in fields)
                existing[k] = v;
        }

        return result;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    // ── Auto-start (Windows registry) ─────────────────────────────────────────

    private static void ApplyAutoStart(bool enable)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "VoxInject";

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        if (key is null) return;

        if (enable)
        {
            var exePath = Environment.ProcessPath ?? string.Empty;
            key.SetValue(appName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(appName, throwOnMissingValue: false);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }
}
