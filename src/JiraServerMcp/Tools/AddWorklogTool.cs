using System.ComponentModel;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira;
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
        + "Jira's decision and not a conversion made here. Logging work reduces the issue's "
        + "remaining estimate by the time logged, which is Jira's own behaviour; pass "
        + "leaveRemainingEstimate to keep the estimate where it is. The entry cannot be edited or "
        + "removed afterwards by this server.")]
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
        // Defaults to false — Jira's own behaviour, which is that a worklog reduces the remaining
        // estimate by what was logged. Defaulting to true would be this server deciding what
        // logging work means to a team's burndown, the same kind of decision as how long a working
        // day is, and that one is already Jira's. Named for the remaining estimate because Jira
        // tracks two and only this one moves.
        [Description(
            "Leave the issue's remaining estimate where it is. Omitted, Jira reduces it by the "
            + "time logged. The original estimate is never touched either way.")]
        bool leaveRemainingEstimate = false,
        [Description(RetrySafeWrite.KeyDescription)]
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

        return await RetrySafeWrite.RunAsync(
            attempts,
            Name,
            idempotencyKey,
            noun: "worklog",
            howToCheck:
                "Read the issue with jira_get_issues and the worklogs expansion before sending it "
                + "again under a new key.",
            profile,
            $"logging work against {key}",
            whenUnreachable: $", and no work was logged against {key}",
            whenTimedOut:
                $". The worklog was sent once and was not repeated, so read {key} with "
                + "jira_get_issues and the worklogs expansion before sending it again.",
            async () =>
            {
                var logged = await jira.AddWorklogAsync(
                    key,
                    timeSpent.Trim(),
                    startedAt,
                    comment,
                    leaveRemainingEstimate,
                    cancellationToken);

                var rendered = new Rendered(
                    $"Logged {logged.TimeSpent} against {key} as worklog {logged.Id}.",
                    ToolOutputs.Node(new AddedWorklogOutput
                    {
                        Outcome = Outcomes.Ok,
                        Key = key,
                        WorklogId = logged.Id,
                        TimeSpent = logged.TimeSpent,
                    }));

                return new Written(rendered, $"worklog {logged.Id} on {key}");
            },
            cancellationToken,
            claim: PermissionAdvice.OnIssue(jira, PermissionAdvice.WorkOnIssues, key));
    }
}
