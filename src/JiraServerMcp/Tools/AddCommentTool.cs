using System.ComponentModel;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>comments:write</c> grant (ADR-0005). There is no tool that edits or
/// deletes a comment, in this grant or any other.
/// </summary>
[McpServerToolType]
internal sealed class AddCommentTool(
    JiraClient jira,
    ServedProfile profile,
    WriteAttempts attempts)
{
    private const string Name = "jira_add_comment";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(AddedCommentOutput))]
    [Description(
        "Add one comment to an issue. The body is Jira wiki markup, written as Jira stores it and "
        + "not converted. The comment cannot be edited or removed afterwards by this server. "
        + "Returns the comment's identifier and the time Jira recorded.")]
    public async Task<CallToolResult> AddCommentAsync(
        [Description("The issue key, such as \"PROJ-42\".")]
        string key,
        [Description("The comment's text, in Jira wiki markup.")]
        string body,
        [Description(
            "An optional idempotency key of the caller's choosing. A second call carrying a key "
            + "this server has already seen writes nothing and reports what became of the first, "
            + "which is what makes a retry after a timeout safe. The record lasts as long as this "
            + "server process and no longer, so a restarted loop is back to reading Jira to find "
            + "out. A key names one attempt: a corrected call after a rejection needs a new one.")]
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return ToolCall.Error(
                $"An empty comment was not added to {key}. Jira refuses one, and there would be "
                + "nothing in it for anyone reading the issue.");
        }

        // Claimed before anything is sent: a key that arrives twice must find the first attempt
        // recorded even when that attempt is what timed out.
        WriteAttempt? attempt = null;

        if (idempotencyKey is { Length: > 0 } supplied)
        {
            if (!attempts.TryBegin(Name, supplied, out var claimed))
            {
                return Replayed(claimed);
            }

            attempt = claimed;
        }

        return await ToolCall.RunAsync(
            profile,
            $"commenting on {key}",
            whenUnreachable: $", and {key} was not commented on",
            whenTimedOut:
                $". The comment was sent once and was not repeated, so read {key} with "
                + "jira_get_issues and the comments expansion before sending it again.",
            async () =>
            {
                var added = await Attempted(
                    attempt,
                    () => jira.AddCommentAsync(key, body, cancellationToken),
                    result => $"comment {result.Id} on {key}");

                // The caller wrote the body; handing it back would be context spent on nothing.
                return new Rendered(
                    $"Added comment {added.Id} to {key} at {added.Created}.",
                    ToolOutputs.Node(new AddedCommentOutput
                    {
                        Outcome = Outcomes.Ok,
                        Key = key,
                        CommentId = added.Id,
                    }));
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs the write and tells the attempt record how it ended. A Jira that answers and refuses
    /// is the one ending that proves nothing was written; anything else — a timeout above all —
    /// leaves the outcome unknown, which is what a repeat needs to be told.
    /// </summary>
    private static async Task<T> Attempted<T>(
        WriteAttempt? attempt,
        Func<Task<T>> write,
        Func<T, string> describe)
    {
        try
        {
            var result = await write();

            attempt?.Succeeded(describe(result));

            return result;
        }
        catch (JiraApiException)
        {
            attempt?.Rejected();

            throw;
        }
    }

    /// <summary>
    /// A key this process has already spent. What the caller may do next differs per ending, so
    /// the three are told apart rather than collapsed into one refusal.
    /// </summary>
    private static CallToolResult Replayed(WriteAttempt prior) => prior.Outcome switch
    {
        WriteOutcome.Ok => ToolCall.Text(new Rendered(
            WriteAttemptAnswers.Ok("comment", prior.Detail ?? "a comment"),
            ToolOutputs.Node(new AddedCommentOutput { Outcome = Outcomes.Ok }))),
        WriteOutcome.Rejected => ToolCall.Error(WriteAttemptAnswers.Rejected("comment")),
        _ => ToolCall.Error(WriteAttemptAnswers.Unknown(
            "comment",
            "Read the issue with jira_get_issues and the comments expansion before sending it "
            + "again under a new key.")),
    };
}
