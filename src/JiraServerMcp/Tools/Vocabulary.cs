namespace JiraServerMcp.Tools;

/// <summary>
/// Matching a caller's word against a published vocabulary — the words one Jira publishes for a
/// kind of choice, such as the transitions available on an issue or the relation phrases this
/// instance publishes. Two tools do it, and the load-bearing part is the ambiguity rule: one
/// status can offer two transitions of the same name going to different statuses, and two link
/// types can publish the same relation phrase, so a word that names two rows resolves to neither.
/// Stated once here, the rule is something a reader can find; performed at two call sites as a
/// count check, it was stated nowhere.
/// </summary>
/// <remarks>
/// Pure: it is handed rows and answers with one of three cases. Reading the published list stays
/// with the tool, because that read is a failure seam and its vocabulary is the tool's
/// (ADR-0008), and so does every refusal sentence, because what an agent can do next differs per
/// vocabulary. Under ADR-0008 clause 3 this is branch-heavy logic lifted out of a tool so it can
/// be proven directly.
///
/// A grammar is not a published vocabulary: <see cref="ProjectKey"/> matches a regex, has two
/// outcomes and no ambiguity case, and stays where it is.
/// </remarks>
internal static class Vocabulary
{
    /// <summary>
    /// Resolves <paramref name="term"/> against the words each row publishes.
    /// </summary>
    /// <param name="rows">The published rows, in publish order.</param>
    /// <param name="wordsOf">
    /// A row's published words, in publish order. The order carries meaning where a row
    /// publishes more than one — a link type's outward wording comes first — which is why the
    /// matched word's index rides back with the row.
    /// </param>
    /// <param name="term">The caller's word. Casing and surrounding space are forgiven.</param>
    /// <typeparam name="T">The row type: a transition, a link type.</typeparam>
    public static Resolved<T> Resolve<T>(
        IReadOnlyList<T> rows,
        Func<T, IReadOnlyList<string>> wordsOf,
        string term)
    {
        // A row matches once, on the first of its words that matches. Relates publishes "relates
        // to" from both ends and is one candidate, not two.
        var matching =
            (from row in rows
             let index = FirstMatch(wordsOf(row), term)
             where index >= 0
             select (Row: row, Index: index)).ToArray();

        return matching switch
        {
            [] => new Unmatched<T>(),
            [var only] => new Matched<T>(only.Row, only.Index),
            _ => new Ambiguous<T>([.. matching.Select(match => match.Row)]),
        };
    }

    /// <summary>
    /// The index of the first word the term matches, or -1. A blank word is no word: a wording
    /// Jira did not send arrives as an empty string, and matching it would resolve the caller's
    /// term to whichever row had a gap in its payload.
    /// </summary>
    private static int FirstMatch(IReadOnlyList<string> words, string term)
    {
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index].Trim();

            if (word.Length > 0
                && string.Equals(word, term.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// What a term resolved to: exactly one row, nothing, or more than one. Three cases rather
    /// than a returned list is the point — a list puts the counting, and so the ambiguity rule,
    /// back at the call site.
    /// </summary>
    public abstract record Resolved<T>;

    /// <summary>
    /// One row, and the index of the word that matched it. A transition publishes one word, so
    /// its index is always zero and nothing reads it; a link type publishes its outward wording
    /// first, so index zero is what says the caller's phrase runs from the first issue to the
    /// second.
    /// </summary>
    public sealed record Matched<T>(T Row, int WordIndex) : Resolved<T>;

    /// <summary>No row publishes the term, which includes the case of no rows at all.</summary>
    public sealed record Unmatched<T> : Resolved<T>;

    /// <summary>The rows that publish the term, in publish order.</summary>
    public sealed record Ambiguous<T>(IReadOnlyList<T> Rows) : Resolved<T>;
}
