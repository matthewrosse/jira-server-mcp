using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Jira;

/// <summary>
/// The issue update, which is the one write here carrying two envelopes: Jira's <c>fields</c>,
/// which sets, and its <c>update</c>, which adds and removes. They ride one PUT, so a call that
/// does both is still one changelog entry — and building them is enough work, in one place, to be
/// a file of its own under ADR-0006's per-file budget.
/// </summary>
public sealed partial class JiraClient
{
    /// <summary>
    /// Changes one issue: the named fields, the values added to and removed from them, and its
    /// assignee, all in the same request. A field whose value is JSON null is cleared; a field not
    /// named is left alone. Jira's <c>fields</c> and <c>update</c> envelopes ride one PUT, so one
    /// call is one changelog entry however many of them it carries. Never retried.
    /// </summary>
    public async Task UpdateIssueAsync(
        string key,
        IReadOnlyDictionary<string, JsonElement> fields,
        IReadOnlyDictionary<string, JsonElement>? add,
        IReadOnlyDictionary<string, JsonElement>? remove,
        JiraAssignee? assignee,
        CancellationToken cancellationToken)
    {
        var body = fields.ToDictionary(
            field => field.Key,
            field => (object?)field.Value,
            StringComparer.Ordinal);

        if (assignee is { } assigned)
        {
            body["assignee"] = assigned.Name is { } name
                ? new Dictionary<string, string> { ["name"] = name }
                : null;
        }

        var update = Operations(add, remove);
        var path = $"rest/api/2/issue/{Uri.EscapeDataString(key)}";

        // Each envelope is sent only when it carries something. An empty one is a claim about
        // nothing, and the endpoint has no reason to be handed one to decide what to do with.
        using var request = (body.Count, update.Count) switch
        {
            (_, 0) => WriteFields(HttpMethod.Put, path, body),
            (0, _) => Write(HttpMethod.Put, path, new { update }),
            _ => Write(HttpMethod.Put, path, new { fields = body, update }),
        };

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Jira's <c>update</c> envelope: a list of single-key operation objects per field. One item is
    /// one operation, so a field given a list of two values is two operations rather than one
    /// carrying a list — which is what Jira does with the value it is handed.
    /// </summary>
    private static Dictionary<string, List<Dictionary<string, JsonElement>>> Operations(
        IReadOnlyDictionary<string, JsonElement>? add,
        IReadOnlyDictionary<string, JsonElement>? remove)
    {
        var update = new Dictionary<string, List<Dictionary<string, JsonElement>>>(
            StringComparer.Ordinal);

        void Collect(string operation, IReadOnlyDictionary<string, JsonElement>? named)
        {
            foreach (var (field, value) in
                     (IEnumerable<KeyValuePair<string, JsonElement>>?)named ?? [])
            {
                var items = value.ValueKind is JsonValueKind.Array
                    ? value.EnumerateArray().ToArray()
                    : [value];

                var operations = update.TryGetValue(field, out var already)
                    ? already
                    : update[field] = [];

                operations.AddRange(items.Select(item =>
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        [operation] = item,
                    }));
            }
        }

        Collect("add", add);
        Collect("remove", remove);

        return update;
    }
}
