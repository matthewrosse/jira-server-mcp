namespace JiraServerMcp.Rendering;

/// <summary>
/// Cutting long text down to a fixed budget. Text is never cut silently: the marker says how much
/// was left behind and which tool returns it, so an agent reading a fragment knows it is one.
/// </summary>
internal static class Truncation
{
    // Kept as the established rendering-test seam; the response budget owns the value.
    public const int Budget = ResponseBudget.LineText;

    // Kept as the established rendering-test seam; the response budget owns the value.
    public const int BodyBudget = ResponseBudget.Prose;

    // Kept as the established rendering-test seam; the response budget owns the value.
    public const int ErrorBudget = ResponseBudget.ErrorText;

    public static string Apply(string text, string issueKey)
    {
        if (text.Length <= ResponseBudget.LineText)
        {
            return text;
        }

        var cut = Cut(text, ResponseBudget.LineText);

        return text[..cut]
               + $"…[truncated, {text.Length - cut} more characters — call jira_get_issues with "
               + $"keys: [\"{issueKey}\"] for the full text]";
    }

    /// <summary>
    /// Prose inside an issue read. The marker names what was left behind but no tool to get it
    /// with: the caller is already reading the issue, and there is nothing further to call.
    /// </summary>
    public static string Body(string text)
    {
        if (text.Length <= ResponseBudget.Prose)
        {
            return text;
        }

        var cut = Cut(text, ResponseBudget.Prose);

        return text[..cut] + $"…[truncated, {text.Length - cut} more characters]";
    }

    /// <summary>
    /// A failed tool call's framed block of Jira's own words. The marker names no follow-up tool:
    /// there is no call that returns the rest of a 500's body.
    /// </summary>
    public static string Error(string text)
    {
        if (text.Length <= ResponseBudget.ErrorText)
        {
            return text;
        }

        var cut = Cut(text, ResponseBudget.ErrorText);

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
