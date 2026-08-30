using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Rendering;

/// <summary>An optional extra section of an issue read, opt-in because each one costs context.</summary>
internal enum Expansion
{
    Comments,
    Transitions,
    Changelog,
    Links,
    Worklogs,
    Attachments,
    Subtasks,
}

/// <summary>
/// Turns the expansions a caller named into the one request that carries them. Every name an
/// expansion travels under — the word an agent asks for, the field it is projected through, the
/// value Jira's own expand parameter takes — is in the table below and nowhere else, because the
/// same string written twice is how a section goes missing while the answer still reads as though
/// there were none of it.
/// </summary>
/// <remarks>
/// The table carries names only. The reader that turns a section into records lives in
/// <c>JiraServerMcp.Jira</c> and the renderer that turns it into text lives here, on opposite
/// sides of ADR-0003's single dependency edge, so no row could hold both.
/// </remarks>
internal static class Expansions
{
    /// <summary>
    /// The words an agent may ask for. A constant because the tool description is an attribute
    /// and cannot interpolate a property; <c>ExpansionTableTests</c> holds it to the table.
    /// </summary>
    public const string Names = "comments, transitions, changelog, links, worklogs, attachments, subtasks";

    /// <summary>
    /// One expansion's names. <paramref name="Field"/> and <paramref name="Expand"/> are the two
    /// halves of the same GET; <paramref name="SeparateRequest"/> is a request of its own, which
    /// is why it is a property of the expansion rather than of whoever asked for it.
    /// </summary>
    public sealed record ExpansionSpec(
        Expansion Id,
        string Name,
        string? Field,
        string? Expand,
        bool SeparateRequest);

    public static readonly IReadOnlyList<ExpansionSpec> Table =
    [
        new(Expansion.Comments, "comments", Field: "comment", Expand: null, SeparateRequest: false),

        // The plain "transitions" form omits the screens; an agent about to name a transition
        // needs to know what that transition will demand of it.
        new(Expansion.Transitions, "transitions", null, "transitions.fields", false),
        new(Expansion.Changelog, "changelog", null, "changelog", false),

        // Links out of Jira are not a field on the issue, so this one expansion answers from the
        // projection and from a second call both.
        new(Expansion.Links, "links", "issuelinks", null, SeparateRequest: true),
        new(Expansion.Worklogs, "worklogs", "worklog", null, false),
        new(Expansion.Attachments, "attachments", "attachment", null, false),

        // Opt-in like every other row, and a break for the one caller that reached sub-tasks by
        // widening the projection with "subtasks": the field is lifted out of the projection now,
        // so that line renders as nothing unless this expansion was asked for. Accepted — the
        // undocumented projection was the defect, and #126 records the reasoning.
        new(Expansion.Subtasks, "subtasks", Field: "subtasks", Expand: null, SeparateRequest: false),
    ];

    /// <summary>
    /// The projected fields Jira answers with as a collection. Every one of them, not only the
    /// ones this call asked for: a field left in the projection renders as a JSON blob, and the
    /// caller is free to widen the projection with a name it never asked for a section of.
    /// </summary>
    public static IReadOnlyList<string> CollectionFields =>
        [.. Table.Select(row => row.Field).OfType<string>()];

    /// <summary>
    /// The expansions named, or the first name that is not one. An unknown name is refused rather
    /// than dropped: silently returning an issue without the section the caller asked for reads as
    /// "there are no comments", which is a different and wrong answer.
    /// </summary>
    public static bool TryParse(
        IReadOnlyList<string>? include,
        out IReadOnlyList<Expansion> expansions,
        out string? unknown)
    {
        var parsed = new List<Expansion>();

        foreach (var name in include ?? [])
        {
            // A JSON array is allowed to carry a null, and it arrives here as one.
            var row = name is null
                ? null
                : Table.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (row is null)
            {
                (expansions, unknown) = ([], name ?? "null");

                return false;
            }

            if (!parsed.Contains(row.Id))
            {
                parsed.Add(row.Id);
            }
        }

        (expansions, unknown) = (parsed, null);

        return true;
    }

    /// <summary>
    /// The one request these expansions travel on: the default field projection, whatever the
    /// caller widened it with, and each expansion's own mechanism.
    /// </summary>
    /// <remarks>
    /// Only the caller's own names go through the alias table. The fields an expansion needs are
    /// this server's, not the caller's, and an operator who happened to alias the name "comment"
    /// would otherwise turn a request for comments into a request for a custom field — which comes
    /// back as an issue with no comments on it, the wrong answer this module exists to prevent.
    /// </remarks>
    public static IssueRead Read(
        IReadOnlyList<Expansion> expansions,
        IReadOnlyList<string>? widen,
        FieldAliases? aliases = null)
    {
        var rows = expansions.Select(Row).ToArray();

        return new IssueRead(
            Fields:
            [
                .. FieldProjection.Widen(widen, aliases),
                .. rows.Select(row => row.Field).OfType<string>(),
            ],
            Expand: [.. rows.Select(row => row.Expand).OfType<string>()],
            CollectionFields: CollectionFields,
            RemoteLinks: rows.Any(row => row.SeparateRequest));
    }

    private static ExpansionSpec Row(Expansion expansion) =>
        Table.First(row => row.Id == expansion);
}
