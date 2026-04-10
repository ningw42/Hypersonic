using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;

using Windows.Security.Credentials;

namespace Hypersonic.Services;

public class CredentialService : ICredentialService
{
    private const string ServerAccountsKey = "ServerAccounts";
    private const string ActiveAccountKey = "ActiveAccount";
    private const string LegacyCredentialKey = "ServerCredential";

    private readonly ILocalSettingsService _settingsService;
    private readonly ILogger _log;

    public CredentialService(ILocalSettingsService settingsService, ILoggingService logService)
    {
        _settingsService = settingsService;
        _log = logService.GetLogger(nameof(CredentialService));
    }

    public async Task<IReadOnlyList<ServerAccount>> GetServerAccountsAsync()
    {
        var accounts = await _settingsService.ReadSettingAsync<List<ServerAccount>>(ServerAccountsKey);
        return accounts?.AsReadOnly() ?? (IReadOnlyList<ServerAccount>)Array.Empty<ServerAccount>();
    }

    public async Task<ServerAccount?> GetActiveServerAccountAsync()
    {
        return await _settingsService.ReadSettingAsync<ServerAccount>(ActiveAccountKey);
    }

    public async Task<ServerCredential?> GetActiveCredentialAsync()
    {
        var account = await GetActiveServerAccountAsync();
        if (account == null || string.IsNullOrWhiteSpace(account.ServerUrl))
        {
            return null;
        }

        var password = RetrievePassword(account.ServerUrl, account.Username);
        if (password == null)
        {
            _log.Warning("Vault entry not found for active account", new { account.ServerUrl, account.Username });
            return null;
        }

        return new ServerCredential
        {
            ServerUrl = account.ServerUrl,
            Username = account.Username,
            Password = password,
        };
    }

    public async Task SaveCredentialAsync(ServerCredential credential)
    {
        // Add to server accounts list (or update if existing).
        var accounts = await _settingsService.ReadSettingAsync<List<ServerAccount>>(ServerAccountsKey)
            ?? new List<ServerAccount>();

        var existing = accounts.FindIndex(a =>
            a.ServerUrl == credential.ServerUrl && a.Username == credential.Username);

        if (existing < 0)
        {
            accounts.Add(new ServerAccount
            {
                ServerUrl = credential.ServerUrl,
                Username = credential.Username,
            });
        }

        await _settingsService.SaveSettingAsync(ServerAccountsKey, accounts);

        // Store password in vault.
        StorePassword(credential.ServerUrl, credential.Username, credential.Password);

        _log.Info("Credential saved", new { credential.ServerUrl, credential.Username });
    }

    public async Task SetActiveServerAsync(string serverUrl, string username)
    {
        var account = new ServerAccount
        {
            ServerUrl = serverUrl,
            Username = username,
        };
        await _settingsService.SaveSettingAsync(ActiveAccountKey, account);
        _log.Info("Active account set", new { serverUrl, username });
    }

    public async Task RemoveCredentialAsync(string serverUrl, string username)
    {
        // Remove from accounts list.
        var accounts = await _settingsService.ReadSettingAsync<List<ServerAccount>>(ServerAccountsKey)
            ?? new List<ServerAccount>();

        accounts.RemoveAll(a => a.ServerUrl == serverUrl && a.Username == username);
        await _settingsService.SaveSettingAsync(ServerAccountsKey, accounts);

        // Remove from vault.
        try
        {
            var vault = new PasswordVault();
            var vaultKey = GetVaultKey(serverUrl);
            var cred = vault.Retrieve(vaultKey, username);
            vault.Remove(cred);
        }
        catch (Exception)
        {
            // Vault entry may already be gone — no-op.
        }

        // Clear active account if it matches.
        var active = await GetActiveServerAccountAsync();
        if (active != null && active.ServerUrl == serverUrl && active.Username == username)
        {
            await _settingsService.RemoveSettingAsync(ActiveAccountKey);
        }

        _log.Info("Credential removed", new { serverUrl, username });
    }

    public async Task MigrateFromLegacyAsync()
    {
        ServerCredential? legacy;
        try
        {
            legacy = await _settingsService.ReadSettingAsync<ServerCredential>(LegacyCredentialKey);
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to read legacy credential", exception: ex);
            return;
        }

        if (legacy == null || string.IsNullOrWhiteSpace(legacy.ServerUrl))
        {
            return;
        }

        _log.Info("Migrating legacy credential", new { legacy.ServerUrl, legacy.Username });

        try
        {
            // Step a: Add to server accounts if not already present.
            var accounts = await _settingsService.ReadSettingAsync<List<ServerAccount>>(ServerAccountsKey)
                ?? new List<ServerAccount>();

            if (!accounts.Any(a => a.ServerUrl == legacy.ServerUrl && a.Username == legacy.Username))
            {
                accounts.Add(new ServerAccount
                {
                    ServerUrl = legacy.ServerUrl,
                    Username = legacy.Username,
                });
                await _settingsService.SaveSettingAsync(ServerAccountsKey, accounts);
            }

            // Step b: Store password in vault if not already present.
            if (RetrievePassword(legacy.ServerUrl, legacy.Username) == null
                && !string.IsNullOrEmpty(legacy.Password))
            {
                StorePassword(legacy.ServerUrl, legacy.Username, legacy.Password);
            }

            // Step c: Set active account if not already set.
            var active = await GetActiveServerAccountAsync();
            if (active == null)
            {
                await SetActiveServerAsync(legacy.ServerUrl, legacy.Username);
            }

            // Step d: Delete legacy key (acts as migration-complete sentinel).
            await _settingsService.RemoveSettingAsync(LegacyCredentialKey);

            _log.Info("Legacy credential migration completed");
        }
        catch (Exception ex)
        {
            _log.Error("Legacy credential migration failed", exception: ex);
        }
    }

    private static string GetVaultKey(string serverUrl) => VaultKeyHelper.GetVaultKey(serverUrl);

    private static void StorePassword(string serverUrl, string username, string password)
    {
        var vault = new PasswordVault();
        var vaultKey = GetVaultKey(serverUrl);

        // Remove any existing credential first to avoid duplicates.
        try
        {
            var existing = vault.Retrieve(vaultKey, username);
            vault.Remove(existing);
        }
        catch (Exception)
        {
            // No existing credential — fine.
        }

        vault.Add(new PasswordCredential(vaultKey, username, password));
    }

    private static string? RetrievePassword(string serverUrl, string username)
    {
        try
        {
            var vault = new PasswordVault();
            var vaultKey = GetVaultKey(serverUrl);
            var cred = vault.Retrieve(vaultKey, username);
            cred.RetrievePassword();
            return cred.Password;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
