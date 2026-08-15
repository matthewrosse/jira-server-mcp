using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Jira;

/// <summary>
/// One page of search results. <see cref="Total"/> is Jira's own count of everything the JQL
/// matched, which is what tells a caller whether asking for another page is worth it.
/// </summary>
public sealed record JiraSearchPage(
    [property: JsonPropertyName("startAt")] int StartAt,
    [property: JsonPropertyName("maxResults")] int MaxResults,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("issues")] IReadOnlyList<JiraIssue> Issues)
{
    /// <summary>Whether Jira has results beyond this page.</summary>
    public bool HasMore => StartAt + Issues.Count < Total;

    /// <summary>The <c>startAt</c> that fetches the next page, when there is one.</summary>
    public int NextStartAt => StartAt + Issues.Count;
}
