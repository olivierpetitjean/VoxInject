using System.Security.Cryptography;
using System.Text;

namespace VoxInject.Core.Services;

/// <summary>
/// Stores secrets encrypted with Windows DPAPI (CurrentUser scope).
/// Blobs are tied to the Windows user account and cannot be decrypted
/// on a different machine or by a different user.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    // Domain separator — not a secret. Prevents cross-application reuse of DPAPI blobs.
    private static readonly byte[] Entropy = [
        0x56, 0x6F, 0x78, 0x49, 0x6E, 0x6A, 0x65, 0x63, 0x74, 0x2D, 0x76, 0x31
    ];

    private readonly string _storageDir;

    public DpapiSecretStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxInject", "secrets"))
    { }

    // Overload for tests
    public DpapiSecretStore(string storageDir)
    {
        _storageDir = storageDir;
        Directory.CreateDirectory(_storageDir);
    }

    public void Save(string purpose, string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentNullException.ThrowIfNull(plaintext);

        var blob = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext),
            Entropy,
            DataProtectionScope.CurrentUser);

        File.WriteAllBytes(SecretPath(purpose), blob);
    }

    public string? Load(string purpose)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        var path = SecretPath(purpose);
        if (!File.Exists(path))
            return null;

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(path),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // Blob is unreadable (e.g. password change in domain/AAD scenario).
            // Return null so the caller can prompt for the value again.
            return null;
        }
    }

    public void Delete(string purpose)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        var path = SecretPath(purpose);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string SecretPath(string purpose)
    {
        // Sanitize: only allow alphanumeric + hyphen to prevent path traversal
        if (!purpose.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new ArgumentException("Purpose contains invalid characters.", nameof(purpose));

        return Path.Combine(_storageDir, $"{purpose}.bin");
    }
}
