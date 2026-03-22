namespace VoxInject.Providers.Abstractions;

/// <summary>
/// Live transcription session created by an <see cref="ITranscriptionProvider"/>.
/// Audio is streamed in as raw PCM-16 chunks; transcripts arrive via events.
/// </summary>
public interface ITranscriptionService : IAsyncDisposable
{
    /// <summary>Partial (non-final) transcript — updates rapidly while the user speaks.</summary>
    event Action<string>? PartialTranscript;

    /// <summary>Final transcript for a completed utterance.</summary>
    event Action<string>? FinalTranscript;

    /// <summary>Unrecoverable session error.</summary>
    event Action<string>? SessionError;

    /// <summary>
    /// Opens the streaming session.
    /// </summary>
    /// <param name="config">
    ///   Provider-specific key/value pairs (API key, endpoint…) as declared by
    ///   <see cref="ITranscriptionProvider.ConfigFields"/>.
    /// </param>
    /// <param name="language">BCP-47 language code (e.g. "fr", "en-US").</param>
    /// <param name="autoPunctuation">Whether to request automatic punctuation.</param>
    /// <param name="wordBoost">Domain-specific vocabulary hints.</param>
    Task StartAsync(
        IReadOnlyDictionary<string, string> config,
        string   language,
        bool     autoPunctuation,
        string[] wordBoost);

    /// <summary>Streams a raw PCM-16 / 16 kHz / mono audio chunk to the provider.</summary>
    Task SendAudioAsync(byte[] pcm16Chunk);

    /// <summary>Gracefully terminates the session and flushes any pending transcripts.</summary>
    Task StopAsync();
}
