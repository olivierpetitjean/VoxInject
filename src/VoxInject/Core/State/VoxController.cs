using VoxInject.Core.Models;
using VoxInject.Core.Services;
using VoxInject.Diagnostics;
using VoxInject.Infrastructure.Win32;
using VoxInject.UI.Overlay;

namespace VoxInject.Core.State;

/// <summary>
/// Central coordinator and state machine.
/// Lifecycle: Idle → Listening → Transcribing → Injecting → Idle
/// </summary>
public sealed class VoxController : IDisposable
{
    private readonly ISettingsService              _settings;
    private readonly ISecretStore                  _secrets;
    private readonly OverlayWindow                 _overlay;
    private readonly FocusTracker                  _focus   = new();
    private readonly AudioCaptureService           _audio   = new();
    private readonly NaudioToneService             _tone    = new();
    private readonly SendInputTextInjectionService _inject  = new();

    private ITranscriptionService? _transcription;
    private volatile VoxState      _state = VoxState.Idle;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool                   _hotkeyHeld;
    private double                 _silenceThresholdDb;
    private int                    _injectedLength;       // chars already injected for current turn
    private bool                   _needsSpaceBetweenTurns;

    public event Action<string>? Error;

    public VoxController(
        ISettingsService settings,
        ISecretStore     secrets,
        OverlayWindow    overlay)
    {
        _settings = settings;
        _secrets  = secrets;
        _overlay  = overlay;
    }

    // ── Hotkey entry points ───────────────────────────────────────────────────

    public async Task OnHotkeyPressedAsync()
    {
        FileLogger.Log($"Hotkey pressed — state={_state}");
        _focus.Snapshot();
        var profile = ActiveProfile();

        if (profile.Mode == RecordingMode.PushToTalk)
        {
            if (_state == VoxState.Idle)
            {
                _hotkeyHeld = true;
                await Task.Run(() => StartListeningAsync(profile)).ConfigureAwait(false);
            }
        }
        else
        {
            if (_state == VoxState.Idle)
                await Task.Run(() => StartListeningAsync(profile)).ConfigureAwait(false);
            else if (_state == VoxState.Listening)
                await Task.Run(StopListeningAsync).ConfigureAwait(false);
        }
    }

    public async Task OnHotkeyReleasedAsync()
    {
        if (ActiveProfile().Mode == RecordingMode.PushToTalk && _hotkeyHeld)
        {
            _hotkeyHeld = false;
            if (_state == VoxState.Listening)
                await Task.Run(StopListeningAsync).ConfigureAwait(false);
        }
    }

    // ── State transitions ─────────────────────────────────────────────────────

    private async Task StartListeningAsync(Profile profile)
    {
        FileLogger.Log("StartListening — waiting for lock");
        if (!await _lock.WaitAsync(0).ConfigureAwait(false)) { FileLogger.Log("StartListening — lock busy, aborting"); return; }
        try
        {
            if (_state != VoxState.Idle) { FileLogger.Log($"StartListening — wrong state {_state}"); return; }

            var apiKey = _secrets.Load("assemblyai-apikey");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Error?.Invoke("No API key configured — open Settings.");
                return;
            }

            FileLogger.Log("StartListening — key OK, connecting WebSocket");
            _state                   = VoxState.Listening;
            _injectedLength          = 0;
            _needsSpaceBetweenTurns  = false;

            if (_settings.Current.ToneEnabled)
                _tone.PlayActivation(_settings.Current.ToneVolume);

            _overlay.Dispatcher.Invoke(() => _overlay.ShowForState(VoxState.Listening));

            _transcription = new AssemblyAiTranscriptionService();
            _transcription.PartialTranscript += OnPartialTranscript;
            _transcription.FinalTranscript   += OnFinalTranscript;
            _transcription.SessionError      += OnSessionError;

            await _transcription.StartAsync(
                apiKey,
                profile.Language,
                profile.AutoPunctuation,
                profile.VocabularyBoost).ConfigureAwait(false);

            FileLogger.Log("StartListening — WebSocket connected, starting audio");
            _audio.AudioChunkReady += OnAudioChunk;
            _audio.LevelChanged    += OnLevelChanged;
            _audio.SilenceDetected += OnSilenceDetected;
            _audio.CaptureFailed   += OnCaptureFailed;

            _silenceThresholdDb = profile.SilenceThresholdDb;
            _audio.Start(
                profile.MicrophoneDeviceId,
                profile.SilenceThresholdDb,
                profile.AutoEnterOnSilence ? profile.SilenceTimeoutMs : int.MaxValue);

            FileLogger.Log($"StartListening — done. AutoEnterOnSilence={profile.AutoEnterOnSilence} SilenceTimeoutMs={profile.SilenceTimeoutMs}");
        }
        catch (Exception ex)
        {
            FileLogger.Log($"StartListening — EXCEPTION: {ex}");
            Error?.Invoke($"Recording error: {ex.Message}");
            ResetToIdleUnsafe();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task StopListeningAsync()
    {
        FileLogger.Log($"StopListening — waiting for lock. state={_state}");
        if (!await _lock.WaitAsync(0).ConfigureAwait(false)) { FileLogger.Log("StopListening — lock busy, aborting"); return; }
        try
        {
            if (_state != VoxState.Listening) { FileLogger.Log($"StopListening — wrong state {_state}"); return; }

            FileLogger.Log("StopListening — A: setting state");
            _state = VoxState.Transcribing;

            FileLogger.Log("StopListening — B: Dispatcher.Invoke UpdateState");
            _overlay.Dispatcher.Invoke(() => _overlay.UpdateState(VoxState.Transcribing));

            FileLogger.Log("StopListening — C: unsubscribing audio");
            _audio.AudioChunkReady -= OnAudioChunk;
            _audio.LevelChanged    -= OnLevelChanged;
            _audio.SilenceDetected -= OnSilenceDetected;
            _audio.CaptureFailed   -= OnCaptureFailed;

            FileLogger.Log("StopListening — D: calling _audio.Stop()");
            _audio.Stop();
            FileLogger.Log("StopListening — E: _audio.Stop() done");

            if (_transcription is not null)
            {
                FileLogger.Log("StopListening — calling StopAsync (2s timeout)");
                await Task.WhenAny(_transcription.StopAsync(), Task.Delay(2000)).ConfigureAwait(false);
                FileLogger.Log("StopListening — StopAsync done");

                _transcription.PartialTranscript -= OnPartialTranscript;
                _transcription.FinalTranscript   -= OnFinalTranscript;
                _transcription.SessionError      -= OnSessionError;

                var tx = _transcription;
                _transcription = null;
                _ = Task.Run(async () => { try { await tx.DisposeAsync().ConfigureAwait(false); } catch { } });
            }

            if (_settings.Current.ToneEnabled)
                _tone.PlayDeactivation(_settings.Current.ToneVolume);

            ResetToIdleUnsafe();
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Stop error: {ex.Message}");
            ResetToIdleUnsafe();
        }
        finally
        {
            _lock.Release();
        }
    }

private void ResetToIdleUnsafe()
    {
        FileLogger.Log("ResetToIdle");
        _state          = VoxState.Idle;
        _injectedLength = 0;
        _focus.Clear();
        _overlay.Dispatcher.Invoke(() =>
        {
            FileLogger.Log("ResetToIdle — calling Hide() on UI thread");
            _overlay.Hide();
            FileLogger.Log($"ResetToIdle — Hide() done. IsVisible={_overlay.IsVisible}");
        });
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnAudioChunk(byte[] chunk)
        => _ = _transcription?.SendAudioAsync(chunk);

    private void OnLevelChanged(double db)
    {
        _overlay.SetAudioLevel(db);
        _overlay.SetSpeaking(db >= _silenceThresholdDb);
    }

    private void OnSilenceDetected()
        => _ = Task.Run(StopListeningAsync);

    private void OnPartialTranscript(string text)
    {
        FileLogger.Log($"Partial: '{text}'");
        InjectDelta(text, appendEnter: false);
    }

    private void OnFinalTranscript(string text)
    {
        FileLogger.Log($"Final: '{text}'");
        InjectDelta(text, appendEnter: ActiveProfile().AutoEnterOnSilence);
        _injectedLength         = 0;
        _needsSpaceBetweenTurns = true;  // next turn starts after a pause → needs a space
    }

    private void InjectDelta(string fullText, bool appendEnter)
    {
        if (fullText.Length <= _injectedLength) return;
        var delta = fullText[_injectedLength..];
        _injectedLength = fullText.Length;

        if (_needsSpaceBetweenTurns)
        {
            _needsSpaceBetweenTurns = false;
            delta = " " + delta;
        }

        _inject.Inject(delta, appendEnter);
    }

    private void OnSessionError(string error)
    {
        FileLogger.Log($"SessionError: {error}");
        Error?.Invoke($"AssemblyAI: {error}");
        _overlay.Dispatcher.Invoke(ResetToIdleUnsafe);
    }

    private void OnCaptureFailed(string error)
    {
        Error?.Invoke($"Microphone error: {error}");
        _overlay.Dispatcher.Invoke(ResetToIdleUnsafe);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Profile ActiveProfile()
    {
        var s = _settings.Current;
        return s.Profiles.FirstOrDefault(p => p.Name == s.ActiveProfileName)
               ?? s.Profiles.FirstOrDefault()
               ?? new Profile();
    }

    public void Dispose()
    {
        _audio.Dispose();
        _lock.Dispose();
        _ = _transcription?.DisposeAsync().AsTask();
    }
}
