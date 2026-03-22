namespace VoxInject.Core.Services;

public interface IAudioCaptureService : IDisposable
{
    /// <summary>Fires with PCM 16-bit LE samples at 16 kHz mono, ready for AssemblyAI.</summary>
    event Action<byte[]>? AudioChunkReady;

    /// <summary>Current RMS level in dBFS. Fires on every captured buffer.</summary>
    event Action<double>? LevelChanged;

    /// <summary>Fires when silence has been sustained beyond the configured threshold.</summary>
    event Action? SilenceDetected;

    void Start(string? deviceId, double silenceThresholdDb, int silenceTimeoutMs);
    void Stop();
}
