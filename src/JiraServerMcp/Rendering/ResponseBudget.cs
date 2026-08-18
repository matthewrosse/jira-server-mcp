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

    /// <summary>
    /// The most entries any one project-detail section is worth. A project that has been running
    /// for years has hundreds of versions, and listing all of them would cost an agent its context
    /// to learn nothing it could not get from the ones it can see.
    /// </summary>
    public const int ProjectSectionCap = 50;

    /// <summary>
    /// The most projects one response is worth. Jira's project endpoint has no page of its own —
    /// it answers with every project at once — so a large instance is cut here or not at all.
    /// </summary>
    public const int ProjectListCap = 100;

    /// <summary>
    /// The most characters one search or agile-page response is worth, about eight thousand
    /// tokens. A hundred issues of ordinary size sit well inside it; a hundred unusually verbose
    /// ones do not, and those are cut off with the position to resume from rather than flooding
    /// the context.
    /// </summary>
    public const int SearchTextBudget = 32_000;

    /// <summary>
    /// The most characters a bulk issue read is worth, matching <see cref="SearchTextBudget"/>: an
    /// issue is not shrunk for company, so what caps the response is how many whole issues fit
    /// rather than a per-issue allowance.
    /// </summary>
    public const int BulkTextBudget = 32_000;

    /// <summary>
    /// Room kept back from the search text budget for the header, the framing, and the closing
    /// marker, none of which can be dropped once the rows have been counted.
    /// </summary>
    public const int PageReserve = 600;

    /// <summary>
    /// The most bytes of one attachment a single fetch is worth, about four thousand tokens of
    /// text. A log or a pasted CSV is routinely larger than anything worth reading in one go, so
    /// the window is a window: the fetch says where it stopped and the next one resumes there,
    /// which is the paging shape the rest of this server already uses.
    /// </summary>
    public const int AttachmentWindow = 16_000;

    /// <summary>
    /// How much of an attachment is read to decide whether it is text at all. Enough to catch the
    /// header of any binary format worth naming, and small enough that deciding costs nothing on
    /// a file that turns out to be unreadable.
    /// </summary>
    public const int AttachmentSniff = 4_096;

    /// <summary>
    /// The most characters a failed tool call's framed block of Jira's own words is worth. A 500
    /// can carry a full stack trace in <c>errorMessages</c>, and this is a rare path by
    /// construction, so it gets the same order as <see cref="Prose"/> rather than a search row's.
    /// </summary>
    public const int ErrorText = 1_000;
}
