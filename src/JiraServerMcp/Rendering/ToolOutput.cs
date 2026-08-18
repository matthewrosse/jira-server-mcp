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

    /// <summary>
    /// A call this server refused rather than performed: an empty comment, a duration Jira could
    /// not read, a transition name no workflow offers, a relation phrase this Jira does not
    /// publish. Some of those are known only after a read, so what this promises is that the write
    /// was never attempted — not that nothing was sent.
    /// </summary>
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
/// The create screen: what a create call must send, and what each field will accept. The most
/// machine-shaped answer this server gives — an agent reads it to build its next call, and every
/// value in it is one that call must send verbatim.
/// </summary>
internal sealed record CreateFieldsOutput : ToolOutput
{
    [JsonPropertyName("projectKey")]
    public string? ProjectKey { get; init; }

    [JsonPropertyName("issueTypeName")]
    public string? IssueTypeName { get; init; }

    /// <summary>
    /// The fields the prose shows, in the order it shows them: required first, then as many
    /// optional ones as the response budget allows. Both halves are cut together.
    /// </summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<CreateFieldOutput>? Fields { get; init; }

    /// <summary>Every field on the create screen, including the optional ones that were cut.</summary>
    [JsonPropertyName("totalFields")]
    public int? TotalFields { get; init; }

    /// <summary>
    /// Whether optional fields were left out. Without it, a field's absence from
    /// <see cref="Fields"/> could mean "not on this screen" or "cut", which is the confusion
    /// <c>hasAllowedValues</c> exists to prevent one level down. Required fields are never cut —
    /// a create fails without every one of them.
    /// </summary>
    [JsonPropertyName("fieldsTruncated")]
    public bool? FieldsTruncated { get; init; }
}

/// <summary>
/// One field on the create screen. The name is a selection label under ADR-0009's amended rule 2:
/// <c>customfield_10010</c> tells an agent nothing, and the name is what makes the identifier it
/// must send actionable.
/// </summary>
internal sealed record CreateFieldOutput
{
    /// <summary>What a create call must send. For a custom field, nothing else will do.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("required")]
    public required bool Required { get; init; }

    /// <summary>
    /// Jira's own <c>schema.type</c>, passed through unchanged and absent where Jira sent none.
    /// Normalising it into a vocabulary this server owns would mean maintaining a mapping across
    /// every Jira Server version, and a mistranslation is worse than an unfamiliar string: an
    /// agent can match an unfamiliar string against the prose, but cannot detect a wrong one.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Whether Jira constrains this field to a list. Kept beside <see cref="AllowedValues"/> so
    /// that "constrained, but the list was cut" stays distinguishable from "unconstrained".
    /// </summary>
    [JsonPropertyName("hasAllowedValues")]
    public required bool HasAllowedValues { get; init; }

    [JsonPropertyName("allowedValues")]
    public IReadOnlyList<string>? AllowedValues { get; init; }

    [JsonPropertyName("allowedValuesTruncated")]
    public bool? AllowedValuesTruncated { get; init; }
}

/// <summary>
/// The projects an account can see. Jira's project endpoint has no page of its own, so what bounds
/// this is a cap rather than a position, and there is nothing to resume from.
/// </summary>
internal sealed record ProjectListOutput : ToolOutput
{
    /// <summary>
    /// The rows in <see cref="Projects"/>, as the page output means it: what is carried here, not
    /// what Jira has. <see cref="TotalCount"/> is the second number.
    /// </summary>
    [JsonPropertyName("count")]
    public int? Count { get; init; }

    /// <summary>Every project Jira answered with, including the ones the cap left out.</summary>
    [JsonPropertyName("totalCount")]
    public int? TotalCount { get; init; }

    /// <summary>
    /// Whether the cap left projects out. There is no next page to ask for: a project outside the
    /// cap is reached by naming its key, or by narrowing with a search.
    /// </summary>
    [JsonPropertyName("cutByCap")]
    public bool? CutByCap { get; init; }

    [JsonPropertyName("projects")]
    public IReadOnlyList<ProjectRowOutput>? Projects { get; init; }
}

/// <summary>
/// One project as a row. The key leads because the key is what every other tool takes as input,
/// and an agent listing projects is almost always looking for one to pass on.
/// </summary>
internal sealed record ProjectRowOutput
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>
/// One project: what a create call may name in it. The names are selection labels under ADR-0009's
/// amended rule 2 — a version name is what <c>fixVersions</c> must be given verbatim, and its id
/// is opaque.
/// </summary>
/// <remarks>
/// The project lead is deliberately absent. It is a username, which rule 2 admits on its face, but
/// nothing branches on it — and rule 1 makes carrying a field permanent while leaving it out stays
/// reversible. The description is prose and is not carried at all.
/// </remarks>
internal sealed record ProjectDetailOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("issueTypeNames")]
    public IReadOnlyList<string>? IssueTypeNames { get; init; }

    [JsonPropertyName("issueTypeCount")]
    public int? IssueTypeCount { get; init; }

    [JsonPropertyName("issueTypesTruncated")]
    public bool? IssueTypesTruncated { get; init; }

    /// <summary>
    /// The most recent versions, which are the ones a create would name — Jira orders them oldest
    /// first, so a cut taken from the front would carry only releases from years ago.
    /// </summary>
    [JsonPropertyName("versionNames")]
    public IReadOnlyList<string>? VersionNames { get; init; }

    [JsonPropertyName("versionCount")]
    public int? VersionCount { get; init; }

    [JsonPropertyName("versionsTruncated")]
    public bool? VersionsTruncated { get; init; }

    [JsonPropertyName("componentNames")]
    public IReadOnlyList<string>? ComponentNames { get; init; }

    [JsonPropertyName("componentCount")]
    public int? ComponentCount { get; init; }

    [JsonPropertyName("componentsTruncated")]
    public bool? ComponentsTruncated { get; init; }
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
