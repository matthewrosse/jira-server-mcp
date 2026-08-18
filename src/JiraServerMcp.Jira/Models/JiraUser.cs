using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// A Jira Server user. <see cref="Name"/> is Jira Server's <c>name</c> field — a username, not
/// the account identifier Jira Cloud returns.
/// </summary>
/// <param name="DisplayName">The name Jira shows for the account, which is prose.</param>
/// <param name="Name">The username, which is what a write must send.</param>
/// <param name="EmailAddress">The address Jira holds, where the account has one.</param>
/// <param name="Active">Whether the account can still log in.</param>
/// <param name="TimeZone">
/// The account's own time zone, as an IANA identifier such as <c>Europe/Warsaw</c>. Jira reads the
/// date literal in a JQL clause in the zone of the account running the query, so this — not the
/// instance's default zone — is what says which window a query asks for. Optional, because a Jira
/// that does not report one must not turn a good answer into a failure.
/// </param>
public sealed record JiraUser(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("emailAddress")] string? EmailAddress,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("timeZone")] string? TimeZone = null);
