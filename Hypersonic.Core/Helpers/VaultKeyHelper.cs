namespace Hypersonic.Core.Helpers;

public static class VaultKeyHelper
{
    private const string VaultKeyPrefix = "Hypersonic:";

    public static string GetVaultKey(string serverUrl) => $"{VaultKeyPrefix}{serverUrl}";
}
