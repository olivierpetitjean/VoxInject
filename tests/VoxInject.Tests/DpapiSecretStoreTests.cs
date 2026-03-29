using VoxInject.Core.Services;
using Xunit;

namespace VoxInject.Tests;

/// <summary>
/// Tests for <see cref="DpapiSecretStore"/>.
/// Uses the test constructor overload that writes to an isolated temp directory.
/// DPAPI is Windows-only; these tests target net8.0-windows intentionally.
/// </summary>
public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string           _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly DpapiSecretStore _store;

    public DpapiSecretStoreTests() => _store = new DpapiSecretStore(_tempDir);

    // ── Roundtrip ─────────────────────────────────────────────────────────────

    [Fact]
    public void Save_ThenLoad_ReturnsOriginalValue()
    {
        _store.Save("my-key", "super-secret");
        Assert.Equal("super-secret", _store.Load("my-key"));
    }

    [Fact]
    public void Save_EmptyString_RoundTrips()
    {
        _store.Save("empty-key", string.Empty);
        Assert.Equal(string.Empty, _store.Load("empty-key"));
    }

    [Fact]
    public void Save_UnicodeValue_RoundTrips()
    {
        const string value = "clé-secrète-🔑";
        _store.Save("unicode-key", value);
        Assert.Equal(value, _store.Load("unicode-key"));
    }

    [Fact]
    public void Save_OverwritesExistingKey()
    {
        _store.Save("key", "v1");
        _store.Save("key", "v2");
        Assert.Equal("v2", _store.Load("key"));
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_UnknownKey_ReturnsNull()
        => Assert.Null(_store.Load("does-not-exist"));

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ExistingKey_LoadReturnsNull()
    {
        _store.Save("to-delete", "value");
        _store.Delete("to-delete");
        Assert.Null(_store.Load("to-delete"));
    }

    [Fact]
    public void Delete_NonExistentKey_DoesNotThrow()
        => _store.Delete("ghost-key"); // must not throw

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public void Save_EmptyPurpose_Throws()
        => Assert.Throws<ArgumentException>(() => _store.Save(string.Empty, "v"));

    [Fact]
    public void Load_EmptyPurpose_Throws()
        => Assert.Throws<ArgumentException>(() => _store.Load(string.Empty));

    [Theory]
    [InlineData("../evil")]          // path traversal
    [InlineData("..\\evil")]         // Windows path traversal
    [InlineData("a/b")]              // forward slash
    [InlineData("a b")]              // space
    [InlineData("key!")]             // special char
    [InlineData("key@domain")]       // at-sign
    public void Save_InvalidPurposeCharacters_ThrowsArgumentException(string purpose)
        => Assert.Throws<ArgumentException>(() => _store.Save(purpose, "v"));

    // ── Isolation ─────────────────────────────────────────────────────────────

    [Fact]
    public void TwoStores_SameDir_ShareData()
    {
        _store.Save("shared", "hello");
        var other = new DpapiSecretStore(_tempDir);
        Assert.Equal("hello", other.Load("shared"));
    }

    [Fact]
    public void TwoStores_DifferentDirs_DoNotShareData()
    {
        var dir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            _store.Save("key", "in-dir-1");
            var other = new DpapiSecretStore(dir2);
            Assert.Null(other.Load("key"));
        }
        finally
        {
            Directory.Delete(dir2, recursive: true);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
