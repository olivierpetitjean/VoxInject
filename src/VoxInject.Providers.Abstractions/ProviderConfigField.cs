namespace VoxInject.Providers.Abstractions;

public enum ProviderFieldType { Text, Password }

/// <summary>
/// Describes a single configuration field exposed by a transcription provider
/// (e.g. API key, base URL, login, password…).
/// </summary>
public sealed record ProviderConfigField(
    string            Key,
    string            Label,
    ProviderFieldType Type        = ProviderFieldType.Text,
    string            Placeholder = "");
