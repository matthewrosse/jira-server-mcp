using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JiraServerMcp.Configuration;

namespace JiraServerMcp.Credentials;

/// <summary>
/// Tokens in an AES-GCM encrypted file, owner-only, with the key beside it: protected by the
/// platform's data-protection API on Windows and by file permissions elsewhere. This protects
/// against casual reading of a backup or a synced directory, and not against a compromised user
/// account — the README says so in those words.
/// </summary>
internal sealed class FileCredentialStore(string configurationDirectory) : ICredentialStore
{
    private const int KeyBytes = 32;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private string File => Path.Combine(configurationDirectory, "credentials.json");

    private string KeyFile => Path.Combine(configurationDirectory, "credentials.key");

    private string Unreadable =>
        $"{File} cannot be read as a credential file. Move it aside and store your tokens again "
        + "with 'jira-server-mcp auth login'.";

    private static string Undecryptable(string profileName) =>
        $"The stored credential for profile '{profileName}' cannot be decrypted. Store the token "
        + $"again with 'jira-server-mcp auth login {profileName}'.";

    public static FileCredentialStore InConfigurationDirectory() =>
        new(ConfigurationPaths.ConfigurationDirectory());

    public Task<string?> GetAsync(string profileName, CancellationToken cancellationToken)
    {
        if (Read().TryGetValue(profileName, out var entry) is not true)
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            using var aes = new AesGcm(ReadKey(profileName), AesGcm.TagByteSizes.MaxSize);

            var ciphertext = Convert.FromBase64String(entry.Ciphertext);
            var plaintext = new byte[ciphertext.Length];

            aes.Decrypt(
                Convert.FromBase64String(entry.Nonce),
                ciphertext,
                Convert.FromBase64String(entry.Tag),
                plaintext,
                // The profile name is authenticated too, so an entry cannot be moved between
                // profiles by editing the file.
                Encoding.UTF8.GetBytes(profileName));

            return Task.FromResult<string?>(Encoding.UTF8.GetString(plaintext));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // A key restored without its credentials, a credentials file restored without its
            // key, or either one edited. The token cannot be recovered, and saying so is more
            // use than a tag-mismatch stack trace.
            throw new ConfigurationException(Undecryptable(profileName));
        }
    }

    public Task SetAsync(string profileName, string personalAccessToken, CancellationToken cancellationToken)
    {
        using var aes = new AesGcm(ReadOrCreateKey(), AesGcm.TagByteSizes.MaxSize);

        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var plaintext = Encoding.UTF8.GetBytes(personalAccessToken);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(profileName));

        var credentials = new Dictionary<string, StoredCredential>(Read(), StringComparer.Ordinal)
        {
            [profileName] = new()
            {
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag),
            },
        };

        Write(credentials);

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string profileName, CancellationToken cancellationToken)
    {
        var credentials = new Dictionary<string, StoredCredential>(Read(), StringComparer.Ordinal);

        if (credentials.Remove(profileName))
        {
            Write(credentials);
        }

        return Task.CompletedTask;
    }

    public string Describe() => $"encrypted file at {File}";

    private Dictionary<string, StoredCredential> Read()
    {
        if (!System.IO.File.Exists(File))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<CredentialFile>(
                       System.IO.File.ReadAllText(File), _serializerOptions)?.Credentials
                   ?? throw new ConfigurationException(Unreadable);
        }
        catch (JsonException)
        {
            throw new ConfigurationException(Unreadable);
        }
    }

    private void Write(Dictionary<string, StoredCredential> credentials)
    {
        ConfigurationPaths.Ensure(configurationDirectory);

        SecureFile.WriteAllText(
            File,
            JsonSerializer.Serialize(new CredentialFile { Credentials = credentials }, _serializerOptions));
    }

    /// <summary>
    /// Reading never mints key material: a missing key file with credentials still in place
    /// means the token is unrecoverable, and quietly writing a fresh key would orphan every
    /// other profile's credential too.
    /// </summary>
    private byte[] ReadKey(string profileName)
    {
        if (!System.IO.File.Exists(KeyFile))
        {
            throw new ConfigurationException(Undecryptable(profileName));
        }

        return Unprotect(System.IO.File.ReadAllBytes(KeyFile));
    }

    private byte[] ReadOrCreateKey()
    {
        ConfigurationPaths.Ensure(configurationDirectory);

        if (System.IO.File.Exists(KeyFile))
        {
            return Unprotect(System.IO.File.ReadAllBytes(KeyFile));
        }

        var created = RandomNumberGenerator.GetBytes(KeyBytes);

        SecureFile.WriteAllBytes(KeyFile, Protect(created));

        return created;
    }

    private static byte[] Protect(byte[] key) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(key, optionalEntropy: null, DataProtectionScope.CurrentUser)
            : key;

    private static byte[] Unprotect(byte[] stored) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(stored, optionalEntropy: null, DataProtectionScope.CurrentUser)
            : stored;

    private sealed class CredentialFile
    {
        public Dictionary<string, StoredCredential> Credentials { get; init; } = [];
    }

    private sealed class StoredCredential
    {
        [JsonRequired]
        public required string Nonce { get; init; }

        [JsonRequired]
        public required string Ciphertext { get; init; }

        [JsonRequired]
        public required string Tag { get; init; }
    }
}
