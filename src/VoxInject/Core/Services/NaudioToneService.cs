using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoxInject.Core.Services;

/// <summary>
/// Generates short sine-wave tones using NAudio.
/// Tones have a short fade-in/out envelope to avoid clicks.
/// </summary>
public sealed class NaudioToneService : IToneService
{
    private const int SampleRate  = 44_100;
    private const int FadeMs      = 12;

    public void PlayActivation(double volume)   => Play(880, 120, volume);
    public void PlayDeactivation(double volume) => Play(660,  80, volume);

    private static void Play(double frequencyHz, int durationMs, double volume)
    {
        // Fire-and-forget on a thread-pool thread so we never block the UI/hotkey thread
        _ = Task.Run(() =>
        {
            try
            {
                var sine    = new SignalGenerator(SampleRate, 1)
                {
                    Type      = SignalGeneratorType.Sin,
                    Frequency = frequencyHz,
                    Gain      = Math.Clamp(volume, 0.0, 1.0)
                };

                var faded   = new FadeInOutSampleProvider(sine);
                faded.BeginFadeIn(FadeMs);

                var samples = (int)(SampleRate * durationMs / 1000.0);
                var buffer  = new float[samples];
                faded.Read(buffer, 0, samples);

                // Fade out the last FadeMs ms
                int fadeOutStart = Math.Max(0, samples - (int)(SampleRate * FadeMs / 1000.0));
                for (int i = fadeOutStart; i < samples; i++)
                {
                    var t = (double)(i - fadeOutStart) / (samples - fadeOutStart);
                    buffer[i] *= (float)(1.0 - t);
                }

                using var waveOut = new WaveOutEvent();
                var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1))
                {
                    DiscardOnBufferOverflow = true
                };

                var bytes = new byte[samples * 4];
                Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
                provider.AddSamples(bytes, 0, bytes.Length);

                waveOut.Init(provider);
                waveOut.Play();

                // Wait for playback to finish
                var wait = durationMs + FadeMs + 20;
                Thread.Sleep(wait);
            }
            catch
            {
                // Tone is non-critical — swallow silently
            }
        });
    }
}
