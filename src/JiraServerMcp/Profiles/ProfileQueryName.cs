using System.Text.RegularExpressions;

namespace JiraServerMcp.Profiles;

/// <summary>
/// What may name a canned query. The name becomes part of a tool name an agent types, so the
/// grammar is Jira-free and MCP-safe: a letter, then lowercase letters, digits and underscores.
/// </summary>
internal static partial class ProfileQueryName
{
    public static bool IsValid(string name) => Grammar().IsMatch(name);

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex Grammar();
}
