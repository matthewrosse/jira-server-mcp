using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// A Jira Server user. <see cref="Name"/> is Jira Server's <c>name</c> field — a username, not
/// the account identifier Jira Cloud returns.
/// </summary>
public sealed record JiraUser(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("emailAddress")] string? EmailAddress,
    [property: JsonPropertyName("active")] bool Active);
