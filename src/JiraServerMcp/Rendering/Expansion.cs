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
}

/// <summary>
/// Turns the expansions a caller named into the one request that carries them. Jira reaches four
/// of these sections through the field projection and two through its own expand parameter, and
/// both travel on the same GET — so asking for all six costs one call, not six.
/// </summary>
internal static class Expansions
{
    private static readonly Dictionary<string, Expansion> _byName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["comments"] = Expansion.Comments,
            ["transitions"] = Expansion.Transitions,
            ["changelog"] = Expansion.Changelog,
            ["links"] = Expansion.Links,
            ["worklogs"] = Expansion.Worklogs,
            ["attachments"] = Expansion.Attachments,
        };

    public static string Names => string.Join(", ", _byName.Keys);

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
            if (name is null || !_byName.TryGetValue(name.Trim(), out var expansion))
            {
                (expansions, unknown) = ([], name ?? "null");

                return false;
            }

            if (!parsed.Contains(expansion))
            {
                parsed.Add(expansion);
            }
        }

        (expansions, unknown) = (parsed, null);

        return true;
    }

    /// <summary>
    /// The field projection this read needs: the default one, whatever the caller widened it with,
    /// and the collection fields that carry three of the sections.
    /// </summary>
    public static IReadOnlyList<string> Fields(
        IReadOnlyList<Expansion> expansions,
        IReadOnlyList<string>? widen) =>
        FieldProjection.Widen([.. widen ?? [], .. expansions.Select(AsField).OfType<string>()]);

    /// <summary>The sections Jira reaches through its own expand parameter.</summary>
    public static IReadOnlyList<string> Expand(IReadOnlyList<Expansion> expansions) =>
        [.. expansions.Select(AsExpand).OfType<string>()];

    private static string? AsField(Expansion expansion) => expansion switch
    {
        Expansion.Comments => "comment",
        Expansion.Links => "issuelinks",
        Expansion.Worklogs => "worklog",
        Expansion.Attachments => "attachment",
        _ => null,
    };

    private static string? AsExpand(Expansion expansion) => expansion switch
    {
        // The plain "transitions" form omits the screens; an agent about to name a transition
        // needs to know what that transition will demand of it.
        Expansion.Transitions => "transitions.fields",
        Expansion.Changelog => "changelog",
        _ => null,
    };
}
