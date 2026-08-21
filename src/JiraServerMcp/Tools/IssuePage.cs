using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tools;

/// <summary>
/// One page of issues, however it was asked for. Six tools answer with one — a JQL search, the
/// canned queries, the change feed, a board's backlog, a sprint — and what differs between them is
/// a query or an identifier, not the paging. The floor under the start position, the clamp on the
/// page size, the widened projection and the render are this project's central promise about what
/// an answer costs an agent, so they are stated here once rather than in each caller.
///
/// It takes the fetch as a delegate rather than a JQL, because all six client methods already
/// agree on the same shape — <c>(startAt, maxResults, fields, ct) -> JiraSearchPage</c> — and a
/// JQL-shaped module would leave the two software-API tools outside it.
///
/// It sits inside <see cref="ToolCall.RunAsync"/> rather than around it: <see cref="ToolCall"/> is
/// the failure seam and this is the paging seam, and a module owning both would need a failure
/// vocabulary that is not its own.
/// </summary>
internal static class IssuePage
{
    public const string StartAtDescription =
        "Zero-based index of the first result to return. Defaults to 0.";

    public const string MaxResultsDescription =
        "How many issues to return. Defaults to 25; more than 100 is clamped to 100.";

    public const string FieldsDescription =
        "Extra field ids to add to the default projection, such as \"description\" or \"customfield_10010\".";

    /// <summary>
    /// How a page of issues is got. The fields are already widened and already resolved through
    /// the profile's aliases, so a fetch knows nothing about either.
    /// </summary>
    public delegate Task<JiraSearchPage> Fetch(
        int startAt,
        int maxResults,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken);

    /// <param name="fetch">The client call that gets the page.</param>
    /// <param name="startAt">The caller's start position, floored here rather than by the caller.</param>
    /// <param name="maxResults">The caller's page size, clamped here rather than by the caller.</param>
    /// <param name="fields">The fields the caller asked to add to the default projection.</param>
    /// <param name="aliases">The operator's names for this Jira's fields.</param>
    /// <param name="watermark">
    /// The change feed's <c>nextSince</c>, computed from the rows the render kept rather than from
    /// the page Jira sent. Absent for every other page of issues, none of which is a feed.
    /// </param>
    /// <param name="prefix">
    /// The line a tool puts above the page — the JQL it authored, the watermark for the next tick.
    /// The words are the tool's; the joining is this module's, because three tools writing that
    /// join by hand is the ceremony this module exists to remove. It is handed the watermark where
    /// there is one, so a tool needs no capture of its own to say it.
    /// </param>
    /// <param name="cancellationToken">The caller's cancellation, passed to the fetch.</param>
    public static async Task<Rendered> RunAsync(
        Fetch fetch,
        int startAt,
        int maxResults,
        string[]? fields,
        FieldAliases aliases,
        Func<IReadOnlyList<JiraIssue>, string>? watermark = null,
        Func<string?, string>? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var page = await fetch(
            Math.Max(startAt, 0),
            Math.Clamp(maxResults, 1, ResponseBudget.LargestPageSize),
            FieldProjection.Widen(fields, aliases),
            cancellationToken);

        // The renderer decides which rows the budget admits, so a watermark taken from inside it is
        // read back out here for the prose: both halves say the same thing because there is only
        // one thing said. Seeded with the answer an empty page gives, so they still agree in the
        // impossible case where the renderer keeps no rows at all.
        var seen = watermark?.Invoke([]);

        var rendered = SearchResults.Render(
            page,
            watermark is null ? null : kept => seen = watermark(kept),
            aliases);

        return prefix is null
            ? rendered
            : new Rendered($"{prefix(seen)}\n{rendered.Text}", rendered.Structure);
    }
}
