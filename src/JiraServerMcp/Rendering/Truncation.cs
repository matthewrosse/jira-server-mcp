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

    public static string Apply(string text, string issueKey)
    {
        if (text.Length <= Budget)
        {
            return text;
        }

        var left = text.Length - Budget;

        return text[..Budget]
               + $"…[truncated, {left} more characters — call jira_get_issue with key "
               + $"{issueKey} for the full text]";
    }
}
