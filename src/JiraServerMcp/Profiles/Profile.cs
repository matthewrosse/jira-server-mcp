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

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
