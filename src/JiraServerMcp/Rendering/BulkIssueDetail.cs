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
    public static string Render(
        IReadOnlyList<BulkIssueResult> results,
        IReadOnlyList<Expansion> expansions)
    {
        var entries = new List<string>();
        var outcomes = new List<string>();
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

                    continue;
                }

                entries.Add(body);
                used += body.Length + 2;
                returned++;

                continue;
            }

            outcomes.Add($"{result.Key}: {Outcome(result.Failure!)}");

            if (JiraWords(result.Failure!) is { Length: > 0 } words)
            {
                entries.Add($"{result.Key}: {Truncation.Body(words)}");
            }
        }

        return UntrustedContent.Envelope(
            Header(results.Count, returned, outcomes),
            string.Join("\n\n", entries));
    }

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
