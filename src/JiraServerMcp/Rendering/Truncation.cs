namespace JiraServerMcp.Rendering;

/// <summary>
/// Cutting long text down to a fixed budget. Text is never cut silently: the marker says how much
/// was left behind and which tool returns it, so an agent reading a fragment knows it is one.
/// </summary>
internal static class Truncation
{
    /// <summary>
    /// The most characters one field's text is worth in a list of issues. A summary is well
    /// inside it; a description pulled in by a widened projection is not, and that is the point.
    /// </summary>
    public const int Budget = 200;

    /// <summary>
    /// The most characters one piece of prose is worth when it is the thing being read rather than
    /// a line in a list. A comment is why a caller asked for the comments, so it gets room a
    /// summary in a search result does not.
    /// </summary>
    public const int BodyBudget = 1_000;

    public static string Apply(string text, string issueKey)
    {
        if (text.Length <= Budget)
        {
            return text;
        }

        var cut = Cut(text, Budget);

        return text[..cut]
               + $"…[truncated, {text.Length - cut} more characters — call jira_get_issue with "
               + $"key {issueKey} for the full text]";
    }

    /// <summary>
    /// Prose inside an issue read. The marker names what was left behind but no tool to get it
    /// with: the caller is already reading the issue, and there is nothing further to call.
    /// </summary>
    public static string Body(string text)
    {
        if (text.Length <= BodyBudget)
        {
            return text;
        }

        var cut = Cut(text, BodyBudget);

        return text[..cut] + $"…[truncated, {text.Length - cut} more characters]";
    }

    /// <summary>
    /// Where to cut without splitting a character in half. A budget counts UTF-16 units, and an
    /// emoji or anything else outside the basic plane occupies two of them; cutting between the
    /// pair leaves a lone surrogate that serialises as a replacement character.
    /// </summary>
    private static int Cut(string text, int budget) =>
        char.IsHighSurrogate(text[budget - 1]) ? budget - 1 : budget;
}
