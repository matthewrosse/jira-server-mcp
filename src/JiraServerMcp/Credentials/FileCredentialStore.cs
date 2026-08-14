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

    public static FileCredentialStore InConfigurationDirectory() =>
        new(ConfigurationPaths.ConfigurationDirectory());

    public Task<string?> GetAsync(string profileName, CancellationToken cancellationToken)
    {
        if (Read().TryGetValue(profileName, out var entry) is not true)
        {
            return Task.FromResult<string?>(null);
        }

        using var aes = new AesGcm(ReadKey(), AesGcm.TagByteSizes.MaxSize);

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

    public Task SetAsync(string profileName, string personalAccessToken, CancellationToken cancellationToken)
    {
        using var aes = new AesGcm(ReadKey(), AesGcm.TagByteSizes.MaxSize);

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

        var document = JsonSerializer.Deserialize<CredentialFile>(
                           System.IO.File.ReadAllText(File), _serializerOptions)
                       ?? throw new InvalidOperationException(
                           $"{File} is empty. Delete it and authenticate again.");

        return document.Credentials;
    }

    private void Write(Dictionary<string, StoredCredential> credentials)
    {
        ConfigurationPaths.Ensure(configurationDirectory);

        SecureFile.WriteAllText(
            File,
            JsonSerializer.Serialize(new CredentialFile { Credentials = credentials }, _serializerOptions));
    }

    private byte[] ReadKey()
    {
        ConfigurationPaths.Ensure(configurationDirectory);

        if (!System.IO.File.Exists(KeyFile))
        {
            var created = RandomNumberGenerator.GetBytes(KeyBytes);

            SecureFile.WriteAllBytes(KeyFile, Protect(created));

            return created;
        }

        return Unprotect(System.IO.File.ReadAllBytes(KeyFile));
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
