using System.ComponentModel;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>worklogs:write</c> grant (ADR-0005). There is no tool that edits or
/// deletes a worklog entry, in this grant or any other.
/// </summary>
[McpServerToolType]
internal sealed class AddWorklogTool(JiraClient jira, ServedProfile profile)
{
    private const string Name = "jira_add_worklog";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false)]
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
        CancellationToken cancellationToken = default)
    {
        // Refused here rather than by Jira, which answers a duration it cannot read with a bare
        // 400 several hundred milliseconds later and no syntax to correct against.
        if (!WorklogInput.IsDuration(timeSpent))
        {
            return Error(
                $"'{timeSpent}' is not a duration Jira can read, so nothing was logged against "
                + $"{key}. Write it in Jira's own syntax, as \"3h 30m\" or \"1w 2d\", with each "
                + "amount naming its unit: w, d, h or m.");
        }

        var startedAt = default(string);

        if (started is not null && !WorklogInput.TryStartTime(started, out startedAt))
        {
            return Error(
                $"'{started}' is not a start time Jira can read, so nothing was logged against "
                + $"{key}. Write it as an ISO-8601 timestamp carrying its offset, such as "
                + "\"2026-08-16T09:00:00+02:00\".");
        }

        try
        {
            var logged = await jira.AddWorklogAsync(
                key,
                timeSpent.Trim(),
                startedAt,
                comment,
                cancellationToken);

            return Text($"Logged {logged.TimeSpent} against {key} as worklog {logged.Id}.");
        }
        catch (JiraApiException exception)
        {
            return Error(
                JiraToolError.Describe(exception, profile.Name, $"logging work against {key}"));
        }
        catch (HttpRequestException exception)
        {
            return Error(
                $"Could not reach Jira, and no work was logged against {key}: {exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(
                $"Jira did not answer for profile '{profile.Name}' in time. The worklog was sent "
                + $"once and was not repeated, so read {key} with jira_get_issue and the worklogs "
                + "expansion before sending it again.");
        }
    }

    private static CallToolResult Text(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string text) =>
        new() { Content = [new TextContentBlock { Text = text }], IsError = true };
}
