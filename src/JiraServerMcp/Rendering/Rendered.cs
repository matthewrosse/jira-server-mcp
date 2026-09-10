using System.Text.Json;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A rendered answer: the prose an agent reads, and the structure a workflow branches on. One
/// traversal produces both (ADR-0009, rule 4) — a second module walking the same model to build
/// the structure would reintroduce exactly the drift the structured half exists to prevent, and
/// where the response budget cuts a page it cuts both halves together.
/// </summary>
/// <param name="Text">The rendered prose, framed and delimited as it always was.</param>
/// <param name="Structure">
/// The structured half, required rather than defaulted. Rule 3 promises structure on every result,
/// so an answer carrying prose alone is a contradiction to refuse where it is written, not a state
/// for <see cref="Tools.ToolCall"/> to repair on the way out.
/// </param>
internal readonly record struct Rendered(string Text, JsonElement Structure);
