using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira;

/// <summary>
/// One page from the software API, which pages differently from the platform API: it says whether
/// this is the last page rather than counting everything there is. A caller therefore learns that
/// more exists, never how much more.
/// </summary>
public sealed record JiraAgilePage<T>(
    [property: JsonPropertyName("startAt")] int StartAt,
    [property: JsonPropertyName("maxResults")] int MaxResults,
    [property: JsonPropertyName("isLast")] bool IsLast,
    [property: JsonPropertyName("values")] IReadOnlyList<T> Values)
{
    /// <summary>Whether Jira has results beyond this page.</summary>
    public bool HasMore => !IsLast;

    /// <summary>The <c>startAt</c> that fetches the next page, when there is one.</summary>
    public int NextStartAt => StartAt + Values.Count;
}
