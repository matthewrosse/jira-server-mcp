using System.Text.RegularExpressions;

namespace JiraServerMcp.Tools;

/// <summary>
/// What a canned query will accept as a project to narrow itself to. The canned queries own their
/// JQL, so the one value a caller contributes to it is checked here rather than in each of them —
/// two copies of a grammar are two things to keep in step, and the sentence a caller reads when
/// they get it wrong is part of the grammar.
/// </summary>
internal static partial class ProjectKey
{
    public static bool IsValid(string key) => Grammar().IsMatch(key);

    /// <summary>
    /// What an agent that guessed most needs: the shape of the thing, and where to go for anything
    /// this shape cannot express.
    /// </summary>
    public static string Rejected(string key) =>
        $"'{key}' is not a valid Jira project key — a project key starts with a letter and "
        + "contains only letters, digits and underscores. Use jira_search for anything else.";

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex Grammar();
}
