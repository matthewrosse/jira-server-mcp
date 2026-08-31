using System.ComponentModel;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>links:write</c> grant (ADR-0005). The link is keyed by its URL, so
/// attaching the same pull request twice updates one link rather than making a second, and the
/// confirmation says which of the two happened.
/// </summary>
[McpServerToolType]
internal sealed class AddRemoteLinkTool(JiraClient jira, ServedProfile profile)
{
    private const string Name = "jira_add_remote_link";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(AddedRemoteLinkOutput))]
    [Description(
        "Attach a URL to an issue — a pull request, a build, a document — so it appears in Jira's "
        + "link panel rather than buried in a comment. The URL is the link's identity: attaching "
        + "the same URL again updates the link that is there, and the answer says whether it was "
        + "created or updated. Read them back with jira_get_issues and the links expansion. There "
        + "is no tool that removes one.")]
    public async Task<CallToolResult> AddRemoteLinkAsync(
        [Description("The issue key, such as \"PROJ-42\".")]
        string key,
        [Description("The URL to attach, which is also what identifies the link on the issue.")]
        string url,
        [Description("The text the link panel shows, such as \"PR #128: retry on 429\".")]
        string title,
        [Description(
            "Jira's grouping header in the link panel, such as \"pull request\". Free text, and "
            + "links sharing one are shown together.")]
        string? relationship = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return ToolCall.Error(
                $"No link was attached to {key}: the URL is what identifies a remote link, and "
                + "an empty one identifies nothing.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return ToolCall.Error(
                $"No link was attached to {key}: the link panel shows the title, and an untitled "
                + "link is a row nobody can read.");
        }

        return await ToolCall.RunAsync(
            profile,
            $"attaching {url} to {key}",
            whenUnreachable: $", and nothing was attached to {key}",
            whenTimedOut:
                $". The link was sent once and was not repeated. Sending it again is safe — the "
                + "URL identifies the link, so a repeat updates rather than duplicates.",
            async () =>
            {
                var created = await jira.AddRemoteLinkAsync(
                    key.Trim(),
                    url.Trim(),
                    title,
                    relationship,
                    cancellationToken);

                // Which of the two it was is the whole value of keying the link by its URL: an
                // agent told "updated" learns that an earlier call of its own already landed. It
                // is a field rather than a second outcome, so that "did this work" stays one
                // equality against ok and the vocabulary does not grow a value per tool.
                return new Rendered(
                    created
                        ? $"Attached {url.Trim()} to {key}."
                        : $"{url.Trim()} was already attached to {key}; its title and relationship "
                          + "were updated. There is one link, not two.",
                    ToolOutputs.Node(new AddedRemoteLinkOutput
                    {
                        Outcome = Outcomes.Ok,
                        Key = key.Trim(),
                        Url = url.Trim(),
                        Created = created,
                    }));
            },
            cancellationToken,
            claim: PermissionAdvice.OnIssue(jira, PermissionAdvice.LinkIssues, key.Trim()));
    }
}
