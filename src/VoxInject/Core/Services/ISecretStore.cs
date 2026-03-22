namespace VoxInject.Core.Services;

public interface ISecretStore
{
    void    Save(string purpose, string plaintext);
    string? Load(string purpose);
    void    Delete(string purpose);
}
