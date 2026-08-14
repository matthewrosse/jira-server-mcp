using System.Text.Json;
using System.Text.Json.Serialization;
using JiraServerMcp.Configuration;

namespace JiraServerMcp.Profiles;

/// <summary>
/// The profile file: one entry per profile, no secrets, readable by the person who owns it.
/// </summary>
internal sealed class ProfileStore(string configurationDirectory)
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private string File => Path.Combine(configurationDirectory, "profiles.json");

    public static ProfileStore InConfigurationDirectory() =>
        new(ConfigurationPaths.ConfigurationDirectory());

    public IReadOnlyDictionary<string, Profile> All() => Read();

    public Profile? Find(string name) => Read().GetValueOrDefault(name);

    public void Add(string name, Profile profile)
    {
        var profiles = new Dictionary<string, Profile>(Read(), StringComparer.Ordinal)
        {
            [name] = profile,
        };

        Write(profiles);
    }

    /// <summary>
    /// Answers whether there was a profile of that name to remove.
    /// </summary>
    public bool Remove(string name)
    {
        var profiles = new Dictionary<string, Profile>(Read(), StringComparer.Ordinal);

        if (!profiles.Remove(name))
        {
            return false;
        }

        Write(profiles);

        return true;
    }

    private Dictionary<string, Profile> Read()
    {
        if (!System.IO.File.Exists(File))
        {
            return [];
        }

        var contents = System.IO.File.ReadAllText(File);

        var document = JsonSerializer.Deserialize<ProfileFile>(contents, _serializerOptions)
            ?? throw new InvalidOperationException($"{File} is empty. Delete it and add the profile again.");

        return document.Profiles;
    }

    private void Write(Dictionary<string, Profile> profiles)
    {
        ConfigurationPaths.Ensure(configurationDirectory);

        SecureFile.WriteAllText(
            File,
            JsonSerializer.Serialize(new ProfileFile { Profiles = profiles }, _serializerOptions));
    }

    private sealed class ProfileFile
    {
        public Dictionary<string, Profile> Profiles { get; init; } = [];
    }
}
