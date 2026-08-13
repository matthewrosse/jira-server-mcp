namespace JiraServerMcp.Jira;

/// <summary>
/// What the client needs to reach one Jira Server. A profile supplies these in a later phase;
/// for now they come from the environment.
/// </summary>
public sealed class JiraClientOptions
{
    public Uri? BaseUrl { get; set; }

    public string? PersonalAccessToken { get; set; }
}
