namespace JiraServerMcp.Jira.Models;

/// <summary>
/// What one issue read asks Jira for. The caller decides all of it — this client publishes no
/// vocabulary of its own for optional sections — and it travels as one value rather than as a
/// handful of parameters, so a caller that adds a section changes one construction and nothing
/// downstream of it.
/// </summary>
/// <param name="Fields">The field projection, including any collection field a section needs.</param>
/// <param name="Expand">The sections Jira reaches through its own expand parameter.</param>
/// <param name="CollectionFields">
/// Which of the projected fields Jira answers with as a collection, and so must be lifted out of
/// the projection and read into a section rather than left in it as a JSON blob. The caller owns
/// this list because it is the same knowledge that decides what may be asked for in the first
/// place; kept here as well it would be the same strings written twice, and a section silently
/// missing is indistinguishable from a section that is empty.
/// </param>
/// <param name="RemoteLinks">
/// Whether to make the second request that carries the issue's links out of Jira. It is not a
/// field on the issue, so it cannot travel on the projection or the expand parameter.
/// </param>
public sealed record IssueRead(
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> Expand,
    IReadOnlyList<string> CollectionFields,
    bool RemoteLinks);
