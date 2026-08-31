using System.Text;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The saved filters this account has favourited, one block each: the id first, because
/// <c>filter = 10001</c> is what a follow-up search sends. A discovery call — nothing here runs a
/// filter.
/// </summary>
internal static class SavedFilterList
{
    /// <summary>
    /// The favourites a name narrowed to, sorted by name and then cut by the cap. Sorted before
    /// cutting because Jira's own order for this endpoint is undocumented: a cap applied to an
    /// unspecified order would cut differently between two identical calls.
    /// </summary>
    public static Rendered Render(IReadOnlyList<JiraSavedFilter> filters, string? startsWith)
    {
        var matched = Matching(filters, startsWith);
        var shown = matched.Take(ResponseBudget.SavedFilterCap).ToArray();

        var body = new StringBuilder();

        foreach (var filter in shown)
        {
            Block(body, filter);
        }

        return new Rendered(
            UntrustedContent.Envelope(
                Header(shown.Length, matched.Count, filters.Count, startsWith),
                body.ToString().TrimEnd()),
            ToolOutputs.Node(new SavedFilterListOutput
            {
                Outcome = Outcomes.Ok,
                Count = shown.Length,
                TotalCount = matched.Count,
                CutByCap = shown.Length < matched.Count,
                Filters =
                [
                    .. shown.Select(filter => new SavedFilterRowOutput
                    {
                        Id = filter.Id,
                        Jql = Truncation.Body(filter.Jql),
                        Owner = filter.Owner?.Name,
                    }),
                ],
            }));
    }

    /// <summary>
    /// An account with no favourites at all. It names the account it asked as, because a personal
    /// access token minted for a service account has no favourites — nobody ever starred a filter
    /// while signed in as it — and "this account has none" and "this is the wrong account" are
    /// otherwise the same answer. The username rather than the display name: it is the identifier,
    /// and it is what a human signs in as to fix this.
    /// </summary>
    public static Rendered NoFavourites(JiraUser account) =>
        new(
            $"No saved filters are favourited by '{account.Name}', the account this server is "
            + "authenticated as. A filter appears here once a human, signed in to Jira as that "
            + "account, stars it — so an account nobody signs in as, such as one a personal access "
            + "token was minted for, has none of its own however many filters the team maintains.",
            ToolOutputs.Node(new SavedFilterListOutput
            {
                Outcome = Outcomes.Ok,
                Count = 0,
                TotalCount = 0,
                CutByCap = false,
                Filters = [],
            }));

    /// <summary>
    /// One filter: the identifier a query names, the name and owner that say what it is and whose
    /// it is, its JQL, and the description where its owner wrote one. The JQL gets a prose budget
    /// rather than a line's — it is the thing being read here, and two thirds of a query narrows
    /// into nothing.
    /// </summary>
    private static void Block(StringBuilder body, JiraSavedFilter filter)
    {
        body.Append(filter.Id).Append(" | ").Append(Truncation.Body(filter.Name));

        if (filter.Owner?.Name is { Length: > 0 } owner)
        {
            body.Append(" | owner ").Append(owner);
        }

        body.AppendLine();
        body.Append("  jql: ").AppendLine(Truncation.Body(filter.Jql));

        if (filter.Description is { Length: > 0 } description)
        {
            body.Append("  ").AppendLine(Truncation.Body(description));
        }
    }

    /// <summary>
    /// The favourites whose name starts with what the caller gave. A prefix rather than a
    /// substring, because that is what the parameter is called and a caller narrowing a list of
    /// names is reading them alphabetically.
    /// </summary>
    private static IReadOnlyList<JiraSavedFilter> Matching(
        IReadOnlyList<JiraSavedFilter> filters,
        string? startsWith)
    {
        var matched = startsWith is { Length: > 0 } prefix
            ? filters.Where(filter =>
                filter.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            : filters;

        return [.. matched.OrderBy(filter => filter.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static string Header(int shown, int matched, int total, string? startsWith)
    {
        if (shown < matched)
        {
            return $"saved filters: {matched} — showing the first {shown} by name. Jira's "
                   + "favourite-filters endpoint has no page of its own, so the rest are not "
                   + "available from this tool; narrow with startsWith instead.";
        }

        return startsWith is { Length: > 0 } prefix
            ? $"saved filters: {matched} of {total} whose name starts with '{prefix}'."
            : $"saved filters: {total}.";
    }
}
