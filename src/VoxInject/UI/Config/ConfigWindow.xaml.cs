using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NAudio.Wave;
using VoxInject.Core.Models;
using VoxInject.Core.Services;

namespace VoxInject.UI.Config;

public partial class ConfigWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly ISecretStore     _secrets;

    private AppSettings   _settings;
    private List<Profile> _profiles;
    private Profile?      _activeProfile;

    // Hotkey capture state
    private uint _capturedModifiers;
    private uint _capturedVk;

    public ConfigWindow(ISettingsService settingsService, ISecretStore secrets)
    {
        _settingsService = settingsService;
        _secrets         = secrets;
        _settings        = settingsService.Current;
        _profiles        = [.._settings.Profiles];

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadApiKey();
        PopulateMicrophones();
        PopulateProfiles();
        LoadGlobalSettings();
    }

    // ── API Key ─────────────────────────────────────────────────────────────

    private void LoadApiKey()
    {
        var key = _secrets.Load("assemblyai-apikey");
        if (!string.IsNullOrEmpty(key))
            ApiKeyBox.Password = key;
    }

    private void RevealToggle_Checked(object sender, RoutedEventArgs e)
    {
        ApiKeyPlain.Text       = ApiKeyBox.Password;
        ApiKeyBox.Visibility   = Visibility.Collapsed;
        ApiKeyPlain.Visibility = Visibility.Visible;
    }

    private void RevealToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Password     = ApiKeyPlain.Text;
        ApiKeyPlain.Visibility = Visibility.Collapsed;
        ApiKeyBox.Visibility   = Visibility.Visible;
    }

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
        MessageBox.Show(
            "Microphone test: speak now — feature coming in next build.",
            "VoxInject",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

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
        LanguageBox.Text  = p.Language;
        VocabBoostBox.Text = string.Join(", ", p.VocabularyBoost);
        AutoPunctCheck.IsChecked = p.AutoPunctuation;

        SelectComboByTag(RecordModeCombo, p.Mode.ToString());
        SelectComboByTag(MicCombo, p.MicrophoneDeviceId);

        AutoEnterCheck.IsChecked = p.AutoEnterOnSilence;
        SilenceBox.Text          = p.SilenceTimeoutMs.ToString();
        SelectComboByTag(EnterKeyCombo, p.UseShiftEnter ? "ShiftEnter" : "Enter");
        SilencePanel.IsEnabled   = p.AutoEnterOnSilence;
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
        SilencePanel.IsEnabled = AutoEnterCheck.IsChecked == true;
    }

    // ── Global settings ───────────────────────────────────────────────────────

    private void LoadGlobalSettings()
    {
        SelectComboByTag(CornerCombo,  _settings.OverlayCorner.ToString());
        SelectComboByTag(ScreenCombo,  _settings.OverlayScreen.ToString());
        OpacitySlider.Value  = _settings.OverlayOpacity;
        SizeSlider.Value     = _settings.OverlaySize;
        ToneCheck.IsChecked  = _settings.ToneEnabled;
        ToneVolumeSlider.Value = _settings.ToneVolume;
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
        if (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl))  mods |= 0x0002; // MOD_CTRL
        if (Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt))   mods |= 0x0001; // MOD_ALT
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= 0x0004; // MOD_SHIFT

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
        // Persist API key securely
        var apiKey = RevealToggle.IsChecked == true ? ApiKeyPlain.Text : ApiKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(apiKey))
            _secrets.Save("assemblyai-apikey", apiKey);

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
            ToneEnabled       = ToneCheck.IsChecked == true,
            ToneVolume        = ToneVolumeSlider.Value,
            AutoStartWithWindows = AutoStartCheck.IsChecked == true
        };

        _settingsService.Save(newSettings);
        ApplyAutoStart(newSettings.AutoStartWithWindows);
        Close();
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
