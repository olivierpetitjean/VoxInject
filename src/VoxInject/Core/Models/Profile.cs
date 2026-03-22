namespace VoxInject.Core.Models;

public sealed record Profile
{
    public string        Name                { get; init; } = "Default";
    public RecordingMode Mode                { get; init; } = RecordingMode.Toggle;
    public bool          AutoEnterOnSilence  { get; init; } = false;
    public int           SilenceTimeoutMs    { get; init; } = 1500;
    public double        SilenceThresholdDb  { get; init; } = -40.0;
    public string        MicrophoneDeviceId  { get; init; } = string.Empty;
    public string        Language            { get; init; } = "fr";
    public bool          AutoPunctuation     { get; init; } = true;
    public string[]      VocabularyBoost     { get; init; } = [];
}
