namespace VoxInject.Core.Services;

public interface ITranscriptionService : IAsyncDisposable
{
    /// <summary>Fires with partial (non-final) transcript text as the user speaks.</summary>
    event Action<string>? PartialTranscript;

    /// <summary>Fires with a final transcript segment (utterance complete).</summary>
    event Action<string>? FinalTranscript;

    /// <summary>Fires if the session encounters an unrecoverable error.</summary>
    event Action<string>? SessionError;

    Task StartAsync(string apiKey, string language, bool autoPunctuation, string[] wordBoost);
    Task SendAudioAsync(byte[] pcm16Chunk);
    Task StopAsync();
}
