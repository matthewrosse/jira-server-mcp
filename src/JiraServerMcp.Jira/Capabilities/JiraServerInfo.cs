using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Capabilities;

/// <summary>
/// The part of Jira's server-info answer the capability probe keeps. Everything else it returns —
/// build number and the instance's own title — is of no use to a tool. The server time is read
/// separately, by <see cref="JiraServerTime"/>, because it is not a fact the probe can record.
/// </summary>
internal sealed record JiraServerInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("deploymentType")] string DeploymentType);

/// <summary>
/// The instance's clock, read on its own because the moment Jira thinks it is changes between
/// calls and the capability probe is recorded once and reused for days.
/// </summary>
internal sealed record JiraServerTime(
    [property: JsonPropertyName("serverTime")] string ServerTime);
