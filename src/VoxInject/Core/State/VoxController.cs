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
    private int                    _injectedLength;
    private bool                   _needsSpaceBetweenTurns;
    private volatile bool          _silenceLocal;       // true = local silence detected
    private volatile bool          _transcriptionDone;  // true = end_of_turn received, waiting for silence

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
        if (!await _lock.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (_state != VoxState.Idle) return;

            var apiKey = _secrets.Load("assemblyai-apikey");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Error?.Invoke("No API key configured — open Settings.");
                return;
            }

            _state                  = VoxState.Listening;
            _injectedLength         = 0;
            _needsSpaceBetweenTurns = false;
            _silenceLocal           = false;
            _transcriptionDone      = false;

            FileLogger.Reset();
            FileLogger.Log($"Session start — AutoEnterOnSilence={profile.AutoEnterOnSilence} SilenceTimeoutMs={profile.SilenceTimeoutMs} ThresholdDb={profile.SilenceThresholdDb}");

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

            _audio.AudioChunkReady += OnAudioChunk;
            _audio.LevelChanged    += OnLevelChanged;
            _audio.SilenceDetected += OnSilenceDetected;
            _audio.CaptureFailed   += OnCaptureFailed;

            _silenceThresholdDb = profile.SilenceThresholdDb;
            _audio.Start(
                profile.MicrophoneDeviceId,
                profile.SilenceThresholdDb,
                profile.AutoEnterOnSilence ? profile.SilenceTimeoutMs : int.MaxValue);
        }
        catch (Exception ex)
        {
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
        if (!await _lock.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (_state != VoxState.Listening) return;

            _state = VoxState.Transcribing;

            _overlay.Dispatcher.Invoke(() => _overlay.UpdateState(VoxState.Transcribing));

            _audio.AudioChunkReady -= OnAudioChunk;
            _audio.LevelChanged    -= OnLevelChanged;
            _audio.SilenceDetected -= OnSilenceDetected;
            _audio.CaptureFailed   -= OnCaptureFailed;

            _audio.Stop();

            if (_transcription is not null)
            {
                await Task.WhenAny(_transcription.StopAsync(), Task.Delay(2000)).ConfigureAwait(false);

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
        _state          = VoxState.Idle;
        _injectedLength = 0;
        _focus.Clear();
        _overlay.Dispatcher.Invoke(() => _overlay.Hide());
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnAudioChunk(byte[] chunk)
        => _ = _transcription?.SendAudioAsync(chunk);

    private void OnLevelChanged(double db)
    {
        _overlay.SetAudioLevel(db);
        var speaking = db >= _silenceThresholdDb;
        _overlay.SetSpeaking(speaking);
        if (speaking) _silenceLocal = false;
    }

    private void OnSilenceDetected()
    {
        _silenceLocal = true;
        FileLogger.Log($"SilenceDetected — transcriptionDone={_transcriptionDone}");
        if (_transcriptionDone)
            PressEnterAfterTurn();
    }

    private void OnPartialTranscript(string text)
    {
        _transcriptionDone = false;
        FileLogger.Log("Partial");
        InjectDelta(text, appendEnter: false);
    }

    private void OnFinalTranscript(string text)
    {
        if (!ActiveProfile().AutoEnterOnSilence)
        {
            FileLogger.Log($"Final (manual): '{text}'");
            InjectDelta(text, appendEnter: false);
            _injectedLength         = 0;
            _needsSpaceBetweenTurns = true;
            return;
        }

        FileLogger.Log($"Final (autoEnter): '{text}' — silenceLocal={_silenceLocal}");
        InjectDelta(text, appendEnter: false);
        _injectedLength = 0;

        if (_silenceLocal)
            PressEnterAfterTurn();
        else
        {
                _transcriptionDone = true;
        }
    }

    private void PressEnterAfterTurn()
    {
        FileLogger.Log("PressEnter");
        _inject.Inject(string.Empty, appendEnter: true);
        _silenceLocal           = false;
        _transcriptionDone      = false;
        _needsSpaceBetweenTurns = false;
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
        FileLogger.Log($"CaptureFailed: {error}");
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
