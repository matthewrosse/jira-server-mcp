using JiraServerMcp.Jira.Capabilities;

namespace JiraServerMcp.Profiles;

/// <summary>
/// One named Jira Server this installation can talk to. The name is the key it is stored under,
/// and a token is never part of it: credentials live in the credential store.
/// </summary>
internal sealed record Profile
{
    public required Uri BaseUrl { get; init; }

    public string? CaBundlePath { get; init; }

    /// <summary>
    /// What the capability probe last found, or null where it has never been taken. Nothing reads
    /// this at startup by asking Jira again: the record is the answer, and `profile refresh`
    /// replaces it.
    /// </summary>
    public JiraCapabilities? Capabilities { get; init; }

    /// <summary>
    /// The operator's own names for this Jira's fields, alias to field identifier. Absent from
    /// every profile written before aliases existed, which reads as none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? FieldAliases { get; init; }

    /// <summary>
    /// The operator's own canned queries, each registering as a tool of its own. Absent from every
    /// profile written before they existed, which reads as none.
    /// </summary>
    public IReadOnlyList<ProfileQuery>? Queries { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// One canned query an operator declared: a fixed JQL this deployment exposes as a named tool, so
/// an agent spends no context authoring a query its team runs every day.
/// </summary>
/// <param name="Name">
/// The operator's name for it, which the tool name is built from. Never the tool name itself: a
/// fixed prefix is what keeps an operator-supplied name from colliding with a built-in tool.
/// </param>
/// <param name="Jql">
/// The query, exactly as Jira will receive it. Checked against Jira when it is declared, because
/// the moment a human is looking at the query they wrote is the moment a mistake is cheap.
/// </param>
/// <param name="Description">
/// What the query is for, in the operator's words. Required: a tool with no description is a tool
/// an agent will not choose, and a generated one would be a lie about intent only the operator
/// knows.
/// </param>
internal sealed record ProfileQuery(string Name, string Jql, string Description);
