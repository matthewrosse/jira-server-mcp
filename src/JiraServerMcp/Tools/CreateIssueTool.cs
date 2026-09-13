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
internal sealed class CreateIssueTool(
    JiraClient jira,
    ServedProfile profile,
    WriteAttempts attempts,
    FieldAliases aliases)
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
        [Description(RetrySafeWrite.KeyDescription)]
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (!aliases.TryResolve(fields, out var resolved, out var collided))
        {
            return ToolCall.Error(Collided(collided!));
        }

        var keyed = new KeyedWrite(Name, "create", WriteRecovery.BySearch());

        return await RetrySafeWrite.RunAsync(
            attempts,
            keyed,
            idempotencyKey,
            profile,
            "creating an issue",
            whenUnreachable: ", and the issue was not created",
            // Whether Jira created it before the wait ran out is not knowable from here, and
            // creating a second one to find out is the failure this server refuses to risk.
            whenTimedOut: WriteRecovery.AfterKeyedTimeout(keyed.Noun, keyed.Recovery),
            async () =>
            {
                var created = await jira.CreateIssueAsync(
                    projectKey,
                    issueType,
                    summary,
                    resolved,
                    cancellationToken);

                return new Written(CreatedIssue.Render(projectKey, created), created.Key);
            },
            cancellationToken,
            describeApiFailure: (exception, permission) =>
                Describe(exception, projectKey, issueType, permission),
            claim: PermissionAdvice.OnProject(jira, PermissionAdvice.CreateIssues, projectKey));
    }

    /// <summary>
    /// A rejected create is the one failure an agent can fix by itself, so the message says which
    /// fields Jira refused and where the project's real requirements are listed.
    /// </summary>
    private string Describe(
        JiraApiException exception,
        string projectKey,
        string issueType,
        PermissionDiagnosis? permission) =>
        JiraToolError.Describe(
            exception,
            profile.Name,
            "creating an issue",
            advice: exception.StatusCode is HttpStatusCode.BadRequest
                ? $"Call jira_get_create_fields with projectKey '{projectKey}' and issueType "
                  + $"'{issueType}' for the fields this project requires and the values they "
                  + "accept."
                  + PermissionPossibility(exception)
                  + FieldAliasAdvice.From(aliases)
                : null,
            permission);

    /// <summary>
    /// A missing create permission and ordinary field validation have the same field-error shape
    /// on Jira Server 8.20.7. The screen remains the first fact to inspect, and only a field the
    /// screen says is writable makes the permission worth investigating. Nothing here claims the
    /// permission is missing, because no lookup is made on this dominant validation path.
    /// </summary>
    private static string PermissionPossibility(JiraApiException exception) =>
        exception.FieldErrors.Count is 0
            ? string.Empty
            : PermissionAdvice.Possibility(PermissionAdvice.CreateIssues, "jira_get_create_fields");


    /// <summary>
    /// One field named twice, once by alias and once by identifier. Refused rather than resolved:
    /// the two values are usually different on purpose, and keeping whichever came last would
    /// write the wrong one with nothing in the answer to say so.
    /// </summary>
    private static string Collided(string collided) =>
        $"{collided} name the same field, and they were given different values, so nothing was "
        + "sent. Name it once — either by its alias or by its identifier.";
}
