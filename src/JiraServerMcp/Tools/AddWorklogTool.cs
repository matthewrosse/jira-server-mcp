using System.ComponentModel;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>worklogs:write</c> grant (ADR-0005). There is no tool that edits or
/// deletes a worklog entry, in this grant or any other.
/// </summary>
[McpServerToolType]
internal sealed class AddWorklogTool(
    JiraClient jira,
    ServedProfile profile,
    WriteAttempts attempts)
{
    private const string Name = "jira_add_worklog";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(AddedWorklogOutput))]
    [Description(
        "Log work against an issue. The time spent is written in Jira's own duration syntax — "
        + "\"3h 30m\", \"1w 2d\", units w, d, h and m — so that how long a working day is stays "
        + "Jira's decision and not a conversion made here. The entry cannot be edited or removed "
        + "afterwards by this server.")]
    public async Task<CallToolResult> AddWorklogAsync(
        [Description("The issue key, such as \"PROJ-42\".")]
        string key,
        [Description("The time spent, in Jira's duration syntax, such as \"3h 30m\".")]
        string timeSpent,
        [Description(
            "When the work started, as an ISO-8601 timestamp carrying its offset, such as "
            + "\"2026-08-16T09:00:00+02:00\". Omitted, Jira records the time it was told.")]
        string? started = null,
        [Description("A note on what the time went on.")]
        string? comment = null,
        [Description(
            "An optional idempotency key of the caller's choosing. A second call carrying a key "
            + "this server has already seen writes nothing and reports what became of the first, "
            + "which is what makes a retry after a timeout safe. The record lasts as long as this "
            + "server process and no longer, so a restarted loop is back to reading Jira to find "
            + "out. A key names one attempt: a corrected call after a rejection needs a new one.")]
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        // Refused here rather than by Jira, which answers a duration it cannot read with a bare
        // 400 several hundred milliseconds later and no syntax to correct against.
        if (!WorklogInput.IsDuration(timeSpent))
        {
            return ToolCall.Error(
                $"'{timeSpent}' is not a duration Jira can read, so nothing was logged against "
                + $"{key}. Write it in Jira's own syntax, as \"3h 30m\" or \"1w 2d\", with each "
                + "amount naming its unit: w, d, h or m.");
        }

        var startedAt = default(string);

        if (started is not null && !WorklogInput.TryStartTime(started, out startedAt))
        {
            return ToolCall.Error(
                $"'{started}' is not a start time Jira can read, so nothing was logged against "
                + $"{key}. Write it as an ISO-8601 timestamp carrying its offset, such as "
                + "\"2026-08-16T09:00:00+02:00\".");
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
            $"logging work against {key}",
            whenUnreachable: $", and no work was logged against {key}",
            whenTimedOut:
                $". The worklog was sent once and was not repeated, so read {key} with "
                + "jira_get_issues and the worklogs expansion before sending it again.",
            async () =>
            {
                var logged = await Attempted(
                    attempt,
                    () => jira.AddWorklogAsync(
                        key,
                        timeSpent.Trim(),
                        startedAt,
                        comment,
                        cancellationToken),
                    result => $"worklog {result.Id} on {key}");

                return new Rendered(
                    $"Logged {logged.TimeSpent} against {key} as worklog {logged.Id}.",
                    ToolOutputs.Node(new AddedWorklogOutput
                    {
                        Outcome = Outcomes.Ok,
                        Key = key,
                        WorklogId = logged.Id,
                        TimeSpent = logged.TimeSpent,
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
            WriteAttemptAnswers.Ok("worklog", prior.Detail ?? "an entry"),
            ToolOutputs.Node(new AddedWorklogOutput { Outcome = Outcomes.Ok }))),
        WriteOutcome.Rejected => ToolCall.Error(WriteAttemptAnswers.Rejected("worklog")),
        _ => ToolCall.Error(WriteAttemptAnswers.Unknown(
            "worklog",
            "Read the issue with jira_get_issues and the worklogs expansion before sending it "
            + "again under a new key.")),
    };
}
