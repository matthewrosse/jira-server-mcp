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

    private string Unreadable =>
        $"{File} cannot be read as a profile file. Move it aside and register your profiles "
        + "again with 'jira-server-mcp profile add'.";

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

        try
        {
            // A file truncated by a crash or edited by hand must not turn every verb, including
            // the one that would repair it, into a stack trace.
            return JsonSerializer.Deserialize<ProfileFile>(contents, _serializerOptions)?.Profiles
                   ?? throw new ConfigurationException(Unreadable);
        }
        catch (JsonException)
        {
            throw new ConfigurationException(Unreadable);
        }
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
