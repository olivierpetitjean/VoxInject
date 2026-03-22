using VoxInject.Providers.Abstractions;

namespace VoxInject.Providers.AssemblyAI;

public sealed class AssemblyAiProvider : ITranscriptionProvider
{
    public string Id          => "assemblyai";
    public string DisplayName => "AssemblyAI";

    public IReadOnlyList<ProviderConfigField> ConfigFields =>
    [
        new ProviderConfigField(
            Key:         "ApiKey",
            Label:       "Clé API",
            Type:        ProviderFieldType.Password,
            Placeholder: "sk-••••••••••••••••")
    ];

    public ITranscriptionService CreateService() => new AssemblyAiTranscriptionService();
}
