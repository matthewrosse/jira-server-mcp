using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Capabilities;

/// <summary>
/// The recorded answer to "what is this Jira and what does it have". Taken once, stored on the
/// profile, and consulted when tools are registered — version-conditional behaviour reads this
/// record and never re-asks Jira.
/// </summary>
/// <param name="Version">Jira's own version string, such as <c>8.20.7</c>.</param>
/// <param name="DeploymentType">
/// What Jira calls itself, <c>Server</c> or <c>Cloud</c>. Recorded rather than acted on: a Data
/// Center licence still reports <c>Server</c>, so it distinguishes nothing this server needs.
/// </param>
/// <param name="SoftwareLicensed">Whether the software API answered.</param>
/// <param name="ProbedAt">When the probe was taken, which is what makes it expire.</param>
public sealed record JiraCapabilities(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("deploymentType")] string DeploymentType,
    [property: JsonPropertyName("softwareLicensed")] bool SoftwareLicensed,
    [property: JsonPropertyName("probedAt")] DateTimeOffset ProbedAt)
{
    /// <summary>
    /// How long a probe is trusted. A Jira gains or loses a Jira Software licence rarely enough
    /// that a week of staleness costs nothing, and often enough that never expiring would leave a
    /// wrong answer in place for good.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether this probe has expired. A stale probe is not an error: it is the best answer there
    /// is until someone refreshes it.
    /// </summary>
    public bool IsStale(DateTimeOffset now) => now - ProbedAt > Lifetime;
}
