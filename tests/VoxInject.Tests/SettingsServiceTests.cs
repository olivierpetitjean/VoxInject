using VoxInject.Core.Models;
using VoxInject.Core.Services;
using Xunit;

namespace VoxInject.Tests;

/// <summary>
/// Tests for <see cref="SettingsService"/>.
/// Uses the test constructor overload that reads/writes to an isolated temp file.
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir  = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private string SettingsPath => Path.Combine(_tempDir, "settings.json");

    // ── Defaults ──────────────────────────────────────────────────────────────

    [Fact]
    public void Current_WhenFileAbsent_ReturnsDefaults()
    {
        var svc = new SettingsService(SettingsPath);
        Assert.Equal("Default",    svc.Current.ActiveProfileName);
        Assert.Single(svc.Current.Profiles);
        Assert.Equal("assemblyai", svc.Current.ActiveProviderId);
    }

    [Fact]
    public void Current_DefaultProfile_HasExpectedValues()
    {
        var svc     = new SettingsService(SettingsPath);
        var profile = svc.Current.Profiles[0];

        Assert.Equal("Default", profile.Name);
        Assert.Equal("fr",      profile.Language);
        Assert.True(profile.AutoPunctuation);
        Assert.Equal(-40.0,     profile.SilenceThresholdDb);
        Assert.Equal(1500,      profile.SilenceTimeoutMs);
    }

    // ── Save / Reload ─────────────────────────────────────────────────────────

    [Fact]
    public void Save_ThenReload_RoundTrips()
    {
        var svc = new SettingsService(SettingsPath);
        svc.Save(svc.Current with { ActiveProfileName = "Travail" });

        svc.Reload();
        Assert.Equal("Travail", svc.Current.ActiveProfileName);
    }

    [Fact]
    public void Save_UpdatesCurrentImmediately()
    {
        var svc = new SettingsService(SettingsPath);
        svc.Save(svc.Current with { ToneEnabled = false });
        Assert.False(svc.Current.ToneEnabled);
    }

    [Fact]
    public void Save_PersistsProfileList()
    {
        var svc      = new SettingsService(SettingsPath);
        var profiles = new List<Profile>
        {
            new() { Name = "Travail", Language = "fr" },
            new() { Name = "Gaming",  Language = "en" },
        };
        svc.Save(svc.Current with { Profiles = profiles, ActiveProfileName = "Travail" });

        var svc2 = new SettingsService(SettingsPath);
        Assert.Equal(2,         svc2.Current.Profiles.Count);
        Assert.Equal("Travail", svc2.Current.Profiles[0].Name);
        Assert.Equal("Gaming",  svc2.Current.Profiles[1].Name);
    }

    [Fact]
    public void Save_PersistsHotkey()
    {
        var svc = new SettingsService(SettingsPath);
        svc.Save(svc.Current with { HotkeyModifiers = 0x0004, HotkeyVk = 0x41 });

        var svc2 = new SettingsService(SettingsPath);
        Assert.Equal(0x0004u, svc2.Current.HotkeyModifiers);
        Assert.Equal(0x0041u, svc2.Current.HotkeyVk);
    }

    [Fact]
    public void Save_PersistsProviderTextConfigs()
    {
        var svc = new SettingsService(SettingsPath);
        var cfg = new Dictionary<string, Dictionary<string, string>>
        {
            ["assemblyai"] = new() { ["endpoint"] = "https://example.com" }
        };
        svc.Save(svc.Current with { ProviderTextConfigs = cfg });

        var svc2 = new SettingsService(SettingsPath);
        Assert.Equal("https://example.com",
            svc2.Current.ProviderTextConfigs["assemblyai"]["endpoint"]);
    }

    // ── Event ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_FiresSettingsChangedEvent()
    {
        var svc       = new SettingsService(SettingsPath);
        AppSettings?  received = null;
        svc.SettingsChanged += (_, s) => received = s;

        svc.Save(svc.Current with { ToneVolume = 0.75 });

        Assert.NotNull(received);
        Assert.Equal(0.75, received!.ToneVolume);
    }

    [Fact]
    public void Save_SettingsChangedEvent_ReflectsNewValues()
    {
        var svc      = new SettingsService(SettingsPath);
        var received = new List<AppSettings>();
        svc.SettingsChanged += (_, s) => received.Add(s);

        svc.Save(svc.Current with { ActiveProfileName = "A" });
        svc.Save(svc.Current with { ActiveProfileName = "B" });

        Assert.Equal(2, received.Count);
        Assert.Equal("A", received[0].ActiveProfileName);
        Assert.Equal("B", received[1].ActiveProfileName);
    }

    // ── Resilience ────────────────────────────────────────────────────────────

    [Fact]
    public void Reload_CorruptedJson_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(SettingsPath, "{ invalid json !!!}}}");

        var svc = new SettingsService(SettingsPath);
        Assert.Equal("Default", svc.Current.ActiveProfileName);
    }

    [Fact]
    public void Reload_EmptyFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(SettingsPath, string.Empty);

        var svc = new SettingsService(SettingsPath);
        Assert.Equal("Default", svc.Current.ActiveProfileName);
    }

    [Fact]
    public void Save_NullSettings_Throws()
    {
        var svc = new SettingsService(SettingsPath);
        Assert.Throws<ArgumentNullException>(() => svc.Save(null!));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
