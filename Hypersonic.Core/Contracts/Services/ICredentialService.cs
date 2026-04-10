using Hypersonic.Core.Models;

namespace Hypersonic.Core.Contracts.Services;

public interface ICredentialService
{
    Task<IReadOnlyList<ServerAccount>> GetServerAccountsAsync();
    Task<ServerAccount?> GetActiveServerAccountAsync();
    Task<ServerCredential?> GetActiveCredentialAsync();
    Task SaveCredentialAsync(ServerCredential credential);
    Task SetActiveServerAsync(string serverUrl, string username);
    Task RemoveCredentialAsync(string serverUrl, string username);
    Task MigrateFromLegacyAsync();
}
