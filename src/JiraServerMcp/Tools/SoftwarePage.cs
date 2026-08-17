using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tools;

/// <summary>
/// What one page of a software API read is worth. The same numbers as a JQL search, for the same
/// reason: every row past them costs the agent context it did not ask for, and a caller wanting
/// the rest pages through them.
/// </summary>
internal static class SoftwarePage
{
    public const int DefaultSize = ResponseBudget.DefaultPageSize;

    public static int Clamp(int maxResults) => Math.Clamp(maxResults, 1, ResponseBudget.LargestPageSize);
}
