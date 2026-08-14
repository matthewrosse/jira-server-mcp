namespace JiraServerMcp.Jira;

/// <summary>
/// What the client needs to reach one Jira Server. A profile supplies these in a later phase;
/// for now they come from the environment.
/// </summary>
public sealed class JiraClientOptions
{
    public Uri? BaseUrl { get; set; }

    public string? PersonalAccessToken { get; set; }

    /// <summary>
    /// A PEM bundle holding the private root that signed this Jira's certificate, for an instance
    /// whose certificate authority is the organisation's own.
    /// </summary>
    public string? CaBundlePath { get; set; }
}
