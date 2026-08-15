using System.Text;
using System.Text.Json;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A page of search results as compact text: one line per issue, the key first so a follow-up
/// call is cheap, and Jira's own wiki markup passed through unconverted — models read it, and
/// converting risks corrupting text that will be written back.
/// </summary>
internal static class SearchResults
{
    /// <summary>
    /// The most characters one search response is worth, about eight thousand tokens. A hundred
    /// issues of ordinary size sit well inside it; a hundred unusually verbose ones do not, and
    /// those are cut off with the position to resume from rather than flooding the context.
    /// </summary>
    public const int ResponseBudget = 32_000;

    /// <summary>
    /// Room kept back from the budget for the header, the framing, and the closing marker, none
    /// of which can be dropped once the issue lines have been counted.
    /// </summary>
    private const int Reserve = 600;

    public static string Render(JiraSearchPage page)
    {
        var lines = new List<string>();
        var used = 0;

        foreach (var issue in page.Issues)
        {
            var line = Line(issue);

            if (used + line.Length + 1 > ResponseBudget - Reserve)
            {
                break;
            }

            lines.Add(line);
            used += line.Length + 1;
        }

        var cutByBudget = lines.Count < page.Issues.Count;

        return $"""
            {Header(page, lines.Count, cutByBudget)}
            {UntrustedContent.Preamble}
            {UntrustedContent.Delimit(string.Join("\n", lines))}
            """;
    }

    private static string Header(JiraSearchPage page, int rendered, bool cutByBudget)
    {
        if (rendered is 0)
        {
            return $"total: {page.Total} — nothing to show on this page.";
        }

        var shown = $"total: {page.Total} — showing {page.StartAt + 1}-{page.StartAt + rendered}";

        if (cutByBudget)
        {
            return $"{shown} — the rest of this page did not fit the response budget; ask for it "
                   + $"with startAt: {page.StartAt + rendered}.";
        }

        return page.HasMore
            ? $"{shown} — more pages exist; ask for the next with startAt: {page.NextStartAt}."
            : $"{shown} — no more pages.";
    }

    private static string Line(JiraIssue issue)
    {
        var line = new StringBuilder(issue.Key);

        foreach (var field in issue.Fields)
        {
            if (Value(field.Value) is not { Length: > 0 } value)
            {
                continue;
            }

            line.Append(" | ").Append(field.Key).Append(": ")
                .Append(Truncation.Apply(value, issue.Key));
        }

        return line.ToString();
    }

    /// <summary>
    /// Jira answers with a different shape per field type: a bare string, an object naming
    /// something, or a list of either. A field Jira left empty is left out rather than rendered as
    /// an empty slot the agent has to read past.
    /// </summary>
    private static string? Value(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
        JsonValueKind.Array => string.Join(
            ", ",
            element.EnumerateArray().Select(Value).OfType<string>()),
        JsonValueKind.Object => Named(element),
        _ => null,
    };

    private static string? Named(JsonElement element)
    {
        foreach (var property in (string[])["name", "displayName", "value", "key"])
        {
            if (element.TryGetProperty(property, out var named)
                && named.ValueKind is JsonValueKind.String)
            {
                return named.GetString();
            }
        }

        // A widened projection can name a custom field whose value is some shape of Jira's own.
        // Its JSON is worth more to an agent than nothing at all.
        return element.GetRawText();
    }
}
