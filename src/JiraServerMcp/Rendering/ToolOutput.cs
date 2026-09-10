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

    /// <summary>
    /// The Jira permission key a refused write claimed and the account turned out not to hold —
    /// <c>EDIT_ISSUES</c>, bare, as the permission-scheme screen spells it. Present only on that
    /// branch: not where the account holds it and not where Jira could not be asked. It may follow
    /// a <c>401</c> or <c>403</c> refusal, the selected comment and worklog <c>400</c> paths, or an
    /// empty transition list; it never appears merely because a failure looked permission-shaped.
    /// A field is added and never removed, so the narrow field keeps the wider one available while
    /// the wider one could not be taken back.
    /// </summary>
    [JsonPropertyOrder(-1)]
    [JsonPropertyName("missingPermission")]
    public string? MissingPermission { get; init; }
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

    /// <summary>
    /// A fault in this server rather than in Jira or the network: something threw that none of the
    /// arms above expected. It is named rather than left to the protocol's own "an error occurred"
    /// because that sentence tells an agent nothing — not whether to retry, not whether anything
    /// was written — and rule 3 promises an outcome on every result, including the results nobody
    /// designed.
    /// </summary>
    public const string Fault = "fault";

    /// <summary>One key of a bulk read that Jira has nothing visible at.</summary>
    public const string NotFound = "not_found";

    /// <summary>One key of a bulk read that was dropped to keep the response affordable.</summary>
    public const string Budget = "budget";
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

    public static JsonElement Outcome(
        string outcome,
        int? statusCode = null,
        string? missingPermission = null) =>
        Node(new OutcomeOutput
        {
            Outcome = outcome,
            StatusCode = statusCode,
            MissingPermission = missingPermission,
        });
}
