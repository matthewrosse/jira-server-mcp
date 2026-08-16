using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Capabilities;

/// <summary>
/// The part of Jira's server-info answer the capability probe keeps. Everything else it returns —
/// build number, server time, the instance's own title — is of no use to a tool.
/// </summary>
internal sealed record JiraServerInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("deploymentType")] string DeploymentType);
