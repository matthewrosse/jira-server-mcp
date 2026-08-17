using JiraServerMcp.Jira;

namespace JiraServerMcp.Profiles;

/// <summary>
/// The mapping from a profile and a personal access token to a configured Jira client: base URL,
/// token, certificate authority bundle path. This is the shape that must never differ between
/// callers — the object lifetime built on top of it is each caller's own concern.
/// </summary>
internal static class ConnectedProfile
{
    public static JiraClientOptions OptionsFor(Profile profile, string token) => new()
    {
        BaseUrl = profile.BaseUrl,
        PersonalAccessToken = token,
        CaBundlePath = profile.CaBundlePath,
    };
}
