namespace JiraServerMcp.Profiles;

/// <summary>
/// One named Jira Server this installation can talk to. The name is the key it is stored under,
/// and a token is never part of it: credentials live in the credential store.
/// </summary>
internal sealed record Profile
{
    public required Uri BaseUrl { get; init; }

    public string? CaBundlePath { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
