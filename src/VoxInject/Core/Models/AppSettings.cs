namespace VoxInject.Core.Models;

public sealed record AppSettings
{
    public string        ActiveProfileName { get; init; } = "Default";
    public List<Profile> Profiles          { get; init; } = [new Profile()];

    // Hotkey: modifiers bitmask (Win32 MOD_* flags) + virtual key code
    public uint HotkeyModifiers { get; init; } = 0x0002 | 0x0001; // MOD_CTRL | MOD_ALT
    public uint HotkeyVk        { get; init; } = 0x56;            // 'V'

    public OverlayCorner OverlayCorner  { get; init; } = OverlayCorner.BottomRight;
    public OverlayScreen OverlayScreen  { get; init; } = OverlayScreen.ActiveScreen;
    public double        OverlayOpacity { get; init; } = 0.92;
    public int           OverlaySize    { get; init; } = 64;       // px, diameter

    public bool   ToneEnabled { get; init; } = true;
    public double ToneVolume  { get; init; } = 0.5;               // 0.0–1.0

    public bool AutoStartWithWindows { get; init; } = false;

    // ── Provider plugin ────────────────────────────────────────────────────────
    /// <summary>Id of the active transcription provider (matches ITranscriptionProvider.Id).</summary>
    public string ActiveProviderId { get; init; } = "assemblyai";

    /// <summary>
    /// Non-sensitive provider config values, keyed by provider id then field key.
    /// Sensitive fields (ProviderFieldType.Password) are stored via ISecretStore
    /// with key "{providerId}.{fieldKey}".
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> ProviderTextConfigs { get; init; } = [];
}
