using System.Text.Json.Serialization;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A URL attached to an issue, and whether this call made the link or updated the one already
/// there.
/// </summary>
internal static class AddedRemoteLink
{
    /// <summary>
    /// Which of the two it was is the whole value of keying the link by its URL: an agent told
    /// "updated" learns that an earlier call of its own already landed. It is a field rather than
    /// a second outcome, so that "did this work" stays one equality against ok and the vocabulary
    /// does not grow a value per tool.
    /// </summary>
    public static Rendered Render(string key, string url, bool created) =>
        new(
            created
                ? $"Attached {url.Trim()} to {key}."
                : $"{url.Trim()} was already attached to {key}; its title and relationship "
                  + "were updated. There is one link, not two.",
            ToolOutputs.Node(new AddedRemoteLinkOutput
            {
                Outcome = Outcomes.Ok,
                Key = key.Trim(),
                Url = url.Trim(),
                Created = created,
            }));
}

/// <summary>
/// A URL attached to an issue. The URL is the link's identity rather than prose about it
/// (ADR-0010), so it is carried; the title and the relationship are text a human typed and are
/// not.
/// </summary>
internal sealed record AddedRemoteLinkOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>
    /// Whether this call made the link or updated the one that was there — the whole value of
    /// keying a remote link by its URL, and the field an agent branches on to learn that an
    /// earlier call of its own already landed. A boolean rather than a second outcome: the outcome
    /// vocabulary answers "did this work, and if not why", and an agent must be able to read
    /// success as one equality rather than as a set of values that grows per tool.
    /// </summary>
    [JsonPropertyName("created")]
    public bool? Created { get; init; }
}
