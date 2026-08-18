using System.Text.RegularExpressions;

namespace JiraServerMcp.Profiles;

/// <summary>
/// What may name a canned query. The name becomes part of a tool name an agent types, so the
/// grammar is Jira-free and MCP-safe: a letter, then lowercase letters, digits and underscores.
/// </summary>
internal static partial class ProfileQueryName
{
    /// <summary>
    /// Bounded as well as shaped: the name becomes part of a tool name, and clients commonly stop
    /// reading one at sixty-four characters. Leaving room for the prefix, this is where a name
    /// stops being a name and starts being a sentence.
    /// </summary>
    public const int Longest = 48;

    public static bool IsValid(string name) => name.Length <= Longest && Grammar().IsMatch(name);

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex Grammar();
}
