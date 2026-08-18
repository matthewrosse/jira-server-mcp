using System.Net;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A bulk issue read as text: one envelope for the whole call rather than one per issue, because
/// the preamble repeated per issue is pure waste and one random marker stops Jira text closing the
/// region early exactly as well as several would. The header names every requested key and its
/// outcome in this server's own words, so a caller can decide what to do next without trusting a
/// character of Jira's text; the region carries the issues and, attributed to its key, any
/// specific sentence Jira said about a failed one.
/// </summary>
internal static class BulkIssueDetail
{
    public static Rendered Render(
        IReadOnlyList<BulkIssueResult> results,
        IReadOnlyList<Expansion> expansions)
    {
        var entries = new List<string>();
        var outcomes = new List<string>();
        var rows = new List<IssueRowOutput>();
        var failures = new List<BulkFailureOutput>();
        var used = 0;
        var budgetExhausted = false;
        var returned = 0;

        foreach (var result in results)
        {
            if (result.Issue is { } issue)
            {
                var body = IssueDetail.Render(issue, expansions);

                if (budgetExhausted
                    || used + body.Length + 2 > ResponseBudget.BulkTextBudget - ResponseBudget.PageReserve)
                {
                    budgetExhausted = true;

                    outcomes.Add(
                        $"{result.Key}: did not fit the response budget — ask for it on its own");

                    failures.Add(new BulkFailureOutput
                    {
                        Key = result.Key,
                        Outcome = Outcomes.Budget,
                    });

                    continue;
                }

                entries.Add(body);
                used += body.Length + 2;
                returned++;

                rows.Add(new IssueRowOutput
                {
                    Key = issue.Key.Length is 0 ? result.Key : issue.Key,
                    StatusId = issue.StatusId,
                    Status = issue.Status,
                    TypeName = issue.TypeName,
                });

                continue;
            }

            outcomes.Add($"{result.Key}: {Outcome(result.Failure!)}");
            failures.Add(Failure(result.Key, result.Failure!));

            if (JiraWords(result.Failure!) is { Length: > 0 } words)
            {
                entries.Add($"{result.Key}: {Truncation.Body(words)}");
            }
        }

        // One shape whether or not isError is set: a partial success is not an error, and a
        // caller must not see the structure appear and vanish with the number of bad keys.
        return new Rendered(
            UntrustedContent.Envelope(
                Header(results.Count, returned, outcomes),
                string.Join("\n\n", entries)),
            ToolOutputs.Node(new BulkIssuesOutput
            {
                Outcome = Outcome(returned, failures),
                Asked = results.Count,
                Returned = returned,
                Issues = rows,
                Failures = failures,
            }));
    }

    /// <summary>
    /// The call's own outcome. A partial success stays <c>ok</c> — some keys came back, and the
    /// ones that did not are in <c>failures</c> — but a call where nothing came back is the one
    /// the tool marks as an error, and reporting that as <c>ok</c> would tell an agent branching
    /// on the outcome that a total failure succeeded.
    /// </summary>
    /// <remarks>
    /// Which failure speaks for the call is the first key's, in the caller's own order. Keys can
    /// fail for different reasons in one call, and no single value can say so: the per-key list is
    /// where mixed causes are read, and it carries every one of them.
    /// </remarks>
    private static string Outcome(int returned, IReadOnlyList<BulkFailureOutput> failures) =>
        returned > 0 || failures.Count is 0 ? Outcomes.Ok : failures[0].Outcome;

    /// <summary>
    /// One key's failure in the outcome vocabulary. A 404 is its own outcome because Jira answers
    /// that way for an issue that does not exist and for one this account cannot see, and neither
    /// is worth retrying; anything else keeps the status code so a caller can tell a 403 from a
    /// 500 without reading the prose.
    /// </summary>
    private static BulkFailureOutput Failure(string key, Exception failure) => failure switch
    {
        JiraApiException { StatusCode: HttpStatusCode.NotFound } =>
            new() { Key = key, Outcome = Outcomes.NotFound },
        OperationCanceledException => new() { Key = key, Outcome = Outcomes.TimedOut },
        JiraApiException exception => new()
        {
            Key = key,
            Outcome = Outcomes.JiraApi,
            StatusCode = (int)exception.StatusCode,
        },
        _ => new() { Key = key, Outcome = Outcomes.Unreachable },
    };

    private static string Header(int asked, int returned, IReadOnlyList<string> outcomes)
    {
        var summary = $"{asked} issue{(asked is 1 ? "" : "s")} asked for, {returned} returned.";

        return outcomes.Count is 0
            ? summary
            : $"{summary} {string.Join(" ", outcomes.Select(outcome => outcome + "."))}";
    }

    private static string Outcome(Exception failure) => failure switch
    {
        JiraApiException { StatusCode: HttpStatusCode.NotFound } => "not found or not visible",
        OperationCanceledException => "timed out",
        JiraApiException exception => $"Jira returned {(int)exception.StatusCode}",
        _ => "failed",
    };

    /// <summary>
    /// Jira's own words for a failed key, or null for a bare 404: Jira answers that one the same
    /// way whether an issue does not exist or is merely invisible, so it has nothing further to
    /// say, exactly as <see cref="JiraToolError"/> treats it.
    /// </summary>
    private static string? JiraWords(Exception failure) =>
        failure is JiraApiException { StatusCode: not HttpStatusCode.NotFound } exception
            ? JiraToolError.JiraWords(exception)
            : null;
}
