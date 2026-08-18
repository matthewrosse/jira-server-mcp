using System.Text.Json;
using System.Text.Json.Serialization;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The structured half of a tool result (ADR-0009): identifiers and the values Jira enumerates,
/// never issue prose. These records are the contract — a field may be added, never removed and
/// never retyped — and they are what the output schemas are generated from, so the shape a client
/// is promised and the shape it receives cannot drift apart.
/// </summary>
/// <remarks>
/// Every property but <see cref="Outcome"/> is optional, because a failed call carries the outcome
/// and nothing else and must still satisfy the tool's declared schema. Anything sourced from
/// Jira's response is optional for the reason ADR-0009 gives: Jira Server versions differ in what
/// they return, and a missing field must not turn a good answer into a protocol error.
/// </remarks>
internal record ToolOutput
{
    /// <summary>What happened, in this server's own vocabulary rather than Jira's.</summary>
    [JsonPropertyOrder(-2)]
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    /// <summary>
    /// The HTTP status Jira answered with, present only on <see cref="Outcomes.JiraApi"/>.
    /// </summary>
    /// <remarks>
    /// Named <c>statusCode</c> rather than <c>status</c>: a transition's confirmation carries the
    /// workflow status it reached under <c>status</c>, and one property cannot be a string on one
    /// result and a number on another without making the schema unusable.
    /// </remarks>
    [JsonPropertyOrder(-1)]
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; init; }
}

/// <summary>
/// The outcome vocabulary. An agent branching on "was this a permissions problem or a dead
/// network" reads this and nothing else.
/// </summary>
internal static class Outcomes
{
    public const string Ok = "ok";

    public const string JiraApi = "jira_api";

    public const string Unreachable = "unreachable";

    public const string TimedOut = "timed_out";

    /// <summary>A call this server refused before anything reached Jira.</summary>
    public const string Refused = "refused";

    /// <summary>One key of a bulk read that Jira has nothing visible at.</summary>
    public const string NotFound = "not_found";

    /// <summary>One key of a bulk read that was dropped to keep the response affordable.</summary>
    public const string Budget = "budget";
}

/// <summary>A page of issues: a search, a canned query, a sprint, or a backlog.</summary>
internal sealed record IssuePageOutput : ToolOutput
{
    /// <summary>Jira's count of everything the query matched, not of what this page carries.</summary>
    [JsonPropertyName("total")]
    public int? Total { get; init; }

    [JsonPropertyName("startAt")]
    public int? StartAt { get; init; }

    /// <summary>The rows in <see cref="Issues"/>, which is what the prose shows too.</summary>
    [JsonPropertyName("count")]
    public int? Count { get; init; }

    /// <summary>Absent when no more pages exist.</summary>
    [JsonPropertyName("nextStartAt")]
    public int? NextStartAt { get; init; }

    /// <summary>Whether the response budget, rather than Jira's page, ended the list.</summary>
    [JsonPropertyName("cutByBudget")]
    public bool? CutByBudget { get; init; }

    [JsonPropertyName("issues")]
    public IReadOnlyList<IssueRowOutput>? Issues { get; init; }
}

/// <summary>
/// One issue as a row. <see cref="Assignee"/> is the username rather than the display name: the
/// username is the identifier a follow-up JQL can use, and the display name is prose.
/// </summary>
internal sealed record IssueRowOutput
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    /// <summary>The status id, which survives an admin renaming the workflow.</summary>
    [JsonPropertyName("statusId")]
    public string? StatusId { get; init; }

    /// <summary>The status name, which is what an agent's prompt talks about.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("typeName")]
    public string? TypeName { get; init; }

    [JsonPropertyName("assignee")]
    public string? Assignee { get; init; }
}

/// <summary>
/// A bulk read, which keeps one shape whether or not <c>isError</c> is set: a partial success is
/// not an error, and the shape must not appear and vanish with the number of bad keys.
/// </summary>
internal sealed record BulkIssuesOutput : ToolOutput
{
    [JsonPropertyName("asked")]
    public int? Asked { get; init; }

    [JsonPropertyName("returned")]
    public int? Returned { get; init; }

    [JsonPropertyName("issues")]
    public IReadOnlyList<IssueRowOutput>? Issues { get; init; }

    [JsonPropertyName("failures")]
    public IReadOnlyList<BulkFailureOutput>? Failures { get; init; }
}

/// <summary>One key that did not come back, and why, in the outcome vocabulary.</summary>
internal sealed record BulkFailureOutput
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; init; }
}

/// <summary>A created issue: what the caller needs to read it back or to say what it made.</summary>
internal sealed record CreatedIssueOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("projectKey")]
    public string? ProjectKey { get; init; }
}

/// <summary><see cref="Changed"/> is the field ids the server sent, as the prose names them.</summary>
internal sealed record UpdatedIssueOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("changed")]
    public IReadOnlyList<string>? Changed { get; init; }
}

/// <summary>
/// <see cref="Status"/> appears only where Jira reported the destination, matching the prose's
/// conditional "now in:" line.
/// </summary>
internal sealed record TransitionedIssueOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("transitionId")]
    public string? TransitionId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

internal sealed record AddedCommentOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("commentId")]
    public string? CommentId { get; init; }
}

/// <summary>
/// <see cref="TimeSpent"/> is the duration as Jira recorded it, which is what says how it read the
/// duration it was given.
/// </summary>
internal sealed record AddedWorklogOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("worklogId")]
    public string? WorklogId { get; init; }

    [JsonPropertyName("timeSpent")]
    public string? TimeSpent { get; init; }
}

/// <summary>
/// The envelope on its own, for a tool whose payload is not yet structured. Rule 3 of ADR-0009 is
/// that structure is present on every result, so these still answer "did this work, and if not
/// why" — they simply carry nothing else yet.
/// </summary>
internal sealed record OutcomeOutput : ToolOutput;

/// <summary>
/// Turns an output record into the node the protocol carries. Renderers build the record, never
/// the node, so the serialized shape and the declared schema come from one definition.
/// </summary>
internal static class ToolOutputs
{
    /// <summary>
    /// A null is an absent field, never a written one: "no more pages" is <c>nextStartAt</c> not
    /// being there, and a client that has to tell null from absent has been given two ways to say
    /// one thing. The outcome leads, because it is the field every reader looks at first.
    /// </summary>
    private static readonly JsonSerializerOptions _options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonElement Node<T>(T output) where T : ToolOutput =>
        JsonSerializer.SerializeToElement(output, _options);

    public static JsonElement Outcome(string outcome, int? statusCode = null) =>
        Node(new OutcomeOutput { Outcome = outcome, StatusCode = statusCode });
}
