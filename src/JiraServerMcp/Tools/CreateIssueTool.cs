using System.ComponentModel;
using System.Net;
using System.Text.Json;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>issues:write</c> grant (ADR-0005).
/// </summary>
[McpServerToolType]
internal sealed class CreateIssueTool(JiraClient jira, ServedProfile profile)
{
    private const string Name = "jira_create_issue";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(CreatedIssueOutput))]
    [Description(
        "Create one issue in a Jira project. Every field beyond the project, the issue type and "
        + "the summary — description, assignee, labels, and custom fields by their identifier — "
        + "goes in the field map, whose values take Jira's own shape: a string for a text field, "
        + "{\"name\": \"jbloggs\"} for a user, {\"id\": \"10300\"} for a select. Read "
        + "jira_get_create_fields first: a project's required custom fields are named only by "
        + "identifier, and a create without them is rejected. Returns the new issue's key.")]
    public async Task<CallToolResult> CreateIssueAsync(
        [Description("The project key the issue is created in, such as \"PROJ\".")]
        string projectKey,
        [Description("The issue type's name, such as \"Bug\", as jira_get_project spells it.")]
        string issueType,
        [Description("The issue's summary — its title.")]
        string summary,
        [Description(
            "Every other field, keyed by Jira's field identifier: \"description\", \"labels\", "
            + "\"customfield_10010\". Values take Jira's own shape.")]
        IReadOnlyDictionary<string, JsonElement>? fields = null,
        CancellationToken cancellationToken = default)
    {
        return await ToolCall.RunAsync(
            profile,
            "creating an issue",
            whenUnreachable: ", and the issue was not created",
            whenTimedOut:
                // Whether Jira created it before the wait ran out is not knowable from here, and
                // creating a second one to find out is the failure this server refuses to risk.
                ". The create was sent once and was not repeated, so the issue may or may not "
                + "exist: search for the summary with jira_search before sending it again.",
            async () =>
            {
                var created = await jira.CreateIssueAsync(
                    projectKey,
                    issueType,
                    summary,
                    fields ?? new Dictionary<string, JsonElement>(),
                    cancellationToken);

                return new Rendered(
                    $"Created {created.Key} (id {created.Id}) in {projectKey}.",
                    ToolOutputs.Node(new CreatedIssueOutput
                    {
                        Outcome = Outcomes.Ok,
                        Key = created.Key,
                        Id = created.Id,
                        ProjectKey = projectKey,
                    }));
            },
            cancellationToken,
            describeApiFailure: exception => Describe(exception, projectKey, issueType));
    }

    /// <summary>
    /// A rejected create is the one failure an agent can fix by itself, so the message says which
    /// fields Jira refused and where the project's real requirements are listed.
    /// </summary>
    private string Describe(JiraApiException exception, string projectKey, string issueType) =>
        JiraToolError.Describe(
            exception,
            profile.Name,
            "creating an issue",
            advice: exception.StatusCode is HttpStatusCode.BadRequest
                ? $"Call jira_get_create_fields with projectKey '{projectKey}' and issueType "
                  + $"'{issueType}' for the fields this project requires and the values they "
                  + "accept."
                : null);
}
