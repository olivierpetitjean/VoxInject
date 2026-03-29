using VoxInject.Infrastructure;
using Xunit;

namespace VoxInject.Tests;

/// <summary>
/// Tests for <see cref="PluginLoader"/>.
/// Verifies directory scanning, filename filtering, and resilience against
/// malformed or incompatible DLLs.
/// </summary>
public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    // ── Directory handling ────────────────────────────────────────────────────

    [Fact]
    public void Load_NonExistentDirectory_ReturnsEmptyList()
    {
        var result = PluginLoader.Load(Path.Combine(_tempDir, "does-not-exist"));
        Assert.Empty(result);
    }

    [Fact]
    public void Load_EmptyDirectory_ReturnsEmptyList()
    {
        Directory.CreateDirectory(_tempDir);
        Assert.Empty(PluginLoader.Load(_tempDir));
    }

    // ── Filename filter ───────────────────────────────────────────────────────

    [Fact]
    public void Load_DllWithWrongNamingPattern_IsIgnored()
    {
        Directory.CreateDirectory(_tempDir);
        // Valid PE header, wrong name — must not be loaded
        File.WriteAllBytes(
            Path.Combine(_tempDir, "SomeRandomLibrary.dll"),
            MzStub());

        Assert.Empty(PluginLoader.Load(_tempDir));
    }

    [Fact]
    public void Load_TextFileMatchingPattern_IsSkippedSilently()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(
            Path.Combine(_tempDir, "VoxInject.Providers.Fake.dll"),
            "this is not a DLL");

        // Must not throw — malformed assemblies are skipped
        var result = PluginLoader.Load(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public void Load_GarbageBytesMatchingPattern_IsSkippedSilently()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllBytes(
            Path.Combine(_tempDir, "VoxInject.Providers.Broken.dll"),
            [0x00, 0x01, 0x02, 0x03, 0x04, 0x05]);

        var result = PluginLoader.Load(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public void Load_DllInSubdirectory_IsNotScanned()
    {
        // Loader uses TopDirectoryOnly — subdirectories must be ignored
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllBytes(
            Path.Combine(subDir, "VoxInject.Providers.Sub.dll"),
            MzStub());

        Assert.Empty(PluginLoader.Load(_tempDir));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Minimal DOS MZ header so the file looks like a PE to the OS.</summary>
    private static byte[] MzStub() => [0x4D, 0x5A, 0x00, 0x00];

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
