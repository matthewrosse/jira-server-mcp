using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Cli;

/// <summary>
/// Running a declared query against Jira once, before it is stored. A query Jira will not accept
/// is refused here, in front of the human who wrote it, rather than months later in front of an
/// agent that can do nothing about it.
/// </summary>
internal static class ProfileQueryCheck
{
    public static async Task<bool> RunsAsync(
        Profile profile,
        string token,
        string jql,
        CancellationToken cancellationToken)
    {
        const string notChecked = ", so the query was not checked and not stored";

        var page = await ConnectedProfile.RunAsync(
            profile,
            token,
            failure => $"{profile.BaseUrl} would not run that query, so it was not stored. "
                       + $"{failure.Message}",
            whenUnreachable: notChecked,
            whenTimedOut: notChecked,
            // The smallest page Jira will serve: what is being checked is whether the query parses
            // and resolves, not what it currently matches.
            client => client.SearchAsync(jql, 0, 1, FieldProjection.Default, cancellationToken),
            cancellationToken);

        return page is not null;
    }
}
