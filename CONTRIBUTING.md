# Contributing to VoxInject

Thanks for your interest. Contributions are welcome — bug fixes, new providers, UI improvements.

## Building locally

```bash
git clone https://github.com/olivierpetitjean/VoxInject.git
cd VoxInject
dotnet build -c Release
dotnet test
```

Requires Windows x64 and [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

---

## Project structure

```
src/
  VoxInject/                      # Main WPF application
    Core/                         # State machine, services, models
    Infrastructure/               # Hotkey, systray, plugin loader
    UI/                           # Overlay window, config window
  VoxInject.Providers.Abstractions/   # Plugin contract (interfaces + records)
  VoxInject.Providers.AssemblyAI/     # Reference provider implementation
tests/
  VoxInject.Tests/                # xUnit unit tests
```

---

## Adding a transcription provider

The fastest way to extend VoxInject is to add a new provider plugin.

### 1. Create a class library

```bash
dotnet new classlib -n VoxInject.Providers.MyProvider -f net8.0
dotnet add VoxInject.Providers.MyProvider reference src/VoxInject.Providers.Abstractions
```

### 2. Implement `ITranscriptionProvider`

```csharp
using VoxInject.Providers.Abstractions;

public sealed class MyProvider : ITranscriptionProvider
{
    public string Id          => "myprovider";          // stable, used in settings
    public string DisplayName => "My Provider";

    public IReadOnlyList<ProviderConfigField> ConfigFields =>
    [
        new("ApiKey", "API Key", ProviderFieldType.Password, "sk-…")
    ];

    public ITranscriptionService CreateService() => new MyTranscriptionService();
}
```

### 3. Implement `ITranscriptionService`

```csharp
public sealed class MyTranscriptionService : ITranscriptionService
{
    public event Action<string>? PartialTranscript;
    public event Action<string>? FinalTranscript;
    public event Action<string>? SessionError;

    public Task StartAsync(IReadOnlyDictionary<string, string> config,
                           string language, bool autoPunctuation, string[] wordBoost)
    {
        // Open a streaming connection to your provider.
        // Fire PartialTranscript / FinalTranscript as results arrive.
        // Fire SessionError on any unrecoverable failure.
    }

    public Task SendAudioAsync(byte[] pcm16Chunk)
    {
        // Forward raw PCM-16 / 16 kHz / mono audio to your provider.
    }

    public Task StopAsync()   { /* graceful shutdown */ }
    public ValueTask DisposeAsync() { /* cleanup */ }
}
```

**Audio contract:** VoxInject delivers PCM-16 audio at 16 kHz mono.
Events must be fired from any thread; the host dispatches UI updates safely.

### 4. Deploy

Build your library and copy the DLL to the `plugins/` folder next to `VoxInject.exe`.
The provider appears automatically in the Settings → API tab on next launch.

---

## Pull requests

- Keep PRs focused — one feature or fix per PR.
- Ensure `dotnet test` passes.
- Follow the existing code style (nullable enabled, records for models, events for cross-layer communication).
- New providers belong in separate repositories or as separate PRs; the main repo only ships the AssemblyAI reference implementation.
