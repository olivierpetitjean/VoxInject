namespace VoxInject.Providers.Abstractions;

/// <summary>
/// Plugin entry point — one implementation per provider DLL.
/// The host discovers implementations via reflection at startup.
/// </summary>
public interface ITranscriptionProvider
{
    /// <summary>Stable machine identifier used in settings (e.g. "assemblyai").</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the UI (e.g. "AssemblyAI").</summary>
    string DisplayName { get; }

    /// <summary>
    /// Ordered list of configuration fields this provider requires.
    /// The host renders them dynamically in the settings window.
    /// </summary>
    IReadOnlyList<ProviderConfigField> ConfigFields { get; }

    /// <summary>Creates a new, unstarted transcription session.</summary>
    ITranscriptionService CreateService();
}
