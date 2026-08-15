namespace JiraServerMcp.Profiles;

/// <summary>
/// The name of the one profile this process serves (ADR-0005). A tool cannot choose a profile,
/// but a failure has to name the one it was talking to, so the user knows which credential to
/// renew.
/// </summary>
internal sealed record ServedProfile(string Name);
