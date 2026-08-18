using System.Text.Json;

namespace JiraServerMcp.Rendering;

/// <summary>
/// What a rendering module answers with: the prose an agent reads, and the structure a workflow
/// branches on. One traversal produces both (ADR-0009, rule 4) — a second module walking the same
/// model to build the structure would reintroduce exactly the drift the structured half exists to
/// prevent, and where the response budget cuts a page it cuts both halves together.
/// </summary>
/// <param name="Text">The rendered prose, framed and delimited as it always was.</param>
/// <param name="Structure">
/// The structured half, or null where this renderer has none yet — <see cref="Tools.ToolCall"/>
/// then supplies the outcome envelope alone, so structure is still present on every result.
/// </param>
internal readonly record struct Rendered(string Text, JsonElement? Structure = null)
{
    /// <summary>
    /// A renderer with no structured half yet. Written as a conversion so the modules that have
    /// not been given one read exactly as they did before.
    /// </summary>
    public static implicit operator Rendered(string text) => new(text);
}
