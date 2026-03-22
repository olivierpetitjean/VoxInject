using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoxInject.Core.Services;

/// <summary>
/// Captures microphone audio via WASAPI, resamples to 16 kHz mono PCM16,
/// computes RMS level, and detects sustained silence.
/// </summary>
public sealed class AudioCaptureService : IAudioCaptureService
{
    private const int TargetSampleRate = 16_000;
    private const int TargetBitDepth   = 16;
    private const int TargetChannels   = 1;

    private WasapiCapture?         _capture;
    private MediaFoundationResampler? _resampler;
    private BufferedWaveProvider?  _buffer;

    private double _silenceThresholdDb;
    private int    _silenceTimeoutMs;
    private long   _silenceStartTick;  // 0 means "not in silence"
    private bool   _silenceFired;
    private bool   _speechHasOccurred; // silence timer only starts after first speech burst

    public event Action<byte[]>? AudioChunkReady;
    public event Action<double>? LevelChanged;
    public event Action?         SilenceDetected;
    public event Action<string>? CaptureFailed;

    public void Start(string? deviceId, double silenceThresholdDb, int silenceTimeoutMs)
    {
        Stop();

        _silenceThresholdDb = silenceThresholdDb;
        _silenceTimeoutMs   = silenceTimeoutMs;
        _silenceStartTick   = 0;
        _silenceFired       = false;
        _speechHasOccurred  = false;

        _capture = CreateCapture(deviceId);

        // Input format from the device
        var inputFormat  = _capture.WaveFormat;

        // Target: 16 kHz, 16-bit, mono
        var outputFormat = new WaveFormat(TargetSampleRate, TargetBitDepth, TargetChannels);

        _buffer    = new BufferedWaveProvider(inputFormat) { DiscardOnBufferOverflow = true };
        _resampler = new MediaFoundationResampler(_buffer, outputFormat) { ResamplerQuality = 60 };

        _capture.DataAvailable   += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (_capture is null) return;

        // Detach events and null references immediately so no more callbacks fire.
        // WasapiCapture.StopRecording() can block for seconds waiting for its internal
        // thread — dispose everything on a background thread so we never block the caller.
        var capture   = _capture;
        var resampler = _resampler;

        _capture.DataAvailable    -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture   = null;
        _resampler = null;
        _buffer    = null;

        // Call audioClient.Stop() immediately via reflection so the OS turns
        // off the mic indicator light right away.  NAudio's StopRecording()
        // and Dispose() both wait for the internal capture thread to exit
        // (~1-2 s) before calling audioClient.Stop(), which keeps the mic
        // light on.  By calling it directly here we decouple the OS-level
        // stop from the thread cleanup, which is then done in the background.
        try
        {
            var field = capture.GetType().GetField(
                "audioClient",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            (field?.GetValue(capture) as NAudio.CoreAudioApi.AudioClient)?.Stop();
        }
        catch { }

        Task.Run(() =>
        {
            try { capture.Dispose(); }    catch { }
            try { resampler?.Dispose(); } catch { }
        });
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Snapshot references to locals — Stop() may null them on another thread
        // between the null-check and the actual use (TOCTOU race).
        var buffer    = _buffer;
        var resampler = _resampler;

        if (e.BytesRecorded == 0 || buffer is null || resampler is null) return;

        buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Calculate exactly how many output bytes correspond to the input bytes
        // received. MediaFoundationResampler does not return 0 when the input
        // buffer is empty — it keeps producing data from internal state. Using a
        // while loop causes audio to be sent at 50-100× real-time speed.
        var inputFormat  = buffer.WaveFormat;
        var outputFormat = resampler.WaveFormat;
        var expectedOutputBytes = (int)((long)e.BytesRecorded
            * outputFormat.AverageBytesPerSecond
            / inputFormat.AverageBytesPerSecond);

        if (expectedOutputBytes < 2) return;

        var outputBuffer = new byte[expectedOutputBytes];
        int read = resampler.Read(outputBuffer, 0, outputBuffer.Length);
        if (read < 2) return;

        var chunk = outputBuffer[..read];
        var db = ComputeRmsDb(chunk);
        LevelChanged?.Invoke(db);
        HandleSilenceDetection(db);
        AudioChunkReady?.Invoke(chunk);
    }

    private static double ComputeRmsDb(byte[] pcm16)
    {
        if (pcm16.Length < 2) return -100.0;

        double sumSq = 0;
        int    count = pcm16.Length / 2;

        for (int i = 0; i < pcm16.Length - 1; i += 2)
        {
            var sample = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            var norm   = sample / 32768.0;
            sumSq     += norm * norm;
        }

        var rms = Math.Sqrt(sumSq / count);
        return 20.0 * Math.Log10(rms + 1e-10);
    }

    private void HandleSilenceDetection(double db)
    {
        if (db >= _silenceThresholdDb)
        {
            // Speech detected — arm the timer and reset it
            _speechHasOccurred = true;
            _silenceStartTick  = 0;
            _silenceFired      = false;
        }
        else if (_speechHasOccurred)
        {
            // Silence after speech — start / check timer
            if (_silenceStartTick == 0)
                _silenceStartTick = Environment.TickCount64;

            if (!_silenceFired &&
                Environment.TickCount64 - _silenceStartTick >= _silenceTimeoutMs)
            {
                _silenceFired = true;
                SilenceDetected?.Invoke();
            }
        }
        // If !_speechHasOccurred, silence at session start is ignored entirely
    }

    private void OnRecordingStopped(object? sender, NAudio.Wave.StoppedEventArgs e)
    {
        if (e.Exception is not null)
            CaptureFailed?.Invoke(e.Exception.Message);
    }

    private static WasapiCapture CreateCapture(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return new WasapiCapture();

        // Try to match by device number (we store the WaveIn index as string)
        if (int.TryParse(deviceId, out var idx) && idx >= 0 && idx < WaveIn.DeviceCount)
        {
            // WasapiCapture doesn't take device index directly; use MMDevice enumeration
            var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var devices    = enumerator.EnumerateAudioEndPoints(
                                 NAudio.CoreAudioApi.DataFlow.Capture,
                                 NAudio.CoreAudioApi.DeviceState.Active);
            if (idx < devices.Count)
                return new WasapiCapture(devices[idx]);
        }

        return new WasapiCapture();
    }

    public void Dispose() => Stop();
}
