namespace JiraServerMcp.Rendering;

/// <summary>
/// What a response is allowed to cost an agent. The rendering and paging mechanics live beside
/// their callers; this module owns only the limits and the reasoning for those limits.
/// </summary>
internal static class ResponseBudget
{
    /// <summary>
    /// The most characters one field's text is worth in a list of issues. A summary is well
    /// inside it; a description pulled in by a widened field projection is not, and that is the
    /// point.
    /// </summary>
    public const int LineText = 200;

    /// <summary>
    /// The most characters one piece of prose is worth when it is the thing being read rather
    /// than a line in a list. A comment is why a caller asked for comments, so it gets room a
    /// summary in a search result does not.
    /// </summary>
    public const int Prose = 1_000;

    /// <summary>
    /// The most entries an issue-read expansion is worth. An issue that has been open for a year
    /// carries hundreds of history groups, and the recent ones are the ones being asked about.
    /// </summary>
    public const int IssueSection = 20;

    /// <summary>The page size that keeps an ordinary search result affordable.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    /// The largest page a caller may request. More results cost context without helping a caller
    /// that can page deliberately.
    /// </summary>
    public const int LargestPageSize = 100;
}
