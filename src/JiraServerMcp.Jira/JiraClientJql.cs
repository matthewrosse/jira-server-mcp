using System.Net.Http.Json;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Jira;

/// <summary>
/// What this Jira will accept in a query. Jira publishes it and nothing else here does: the JQL
/// name of a custom field is neither the identifier the write endpoints take nor, in general, its
/// bare display name, and the operators a field accepts differ per field.
/// </summary>
/// <remarks>
/// Nothing here is cached. The answer changes as an administrator adds a field, and — measured on
/// the harness instance — it also changes as an instance warms up: the same endpoint answered with
/// 28 fields and no custom ones while seeding was running, and with 70 fields and 13 custom
/// minutes later. A copy taken at the first moment is wrong for as long as it is kept, and nothing
/// about the wrong answer looks wrong.
/// </remarks>
public sealed partial class JiraClient
{
    /// <summary>Every field this account may name in a query, and the functions this instance publishes.</summary>
    public async Task<JiraJqlCatalogue> GetJqlFieldsAsync(CancellationToken cancellationToken)
    {
        using var document = await JsonAsync("rest/api/2/jql/autocompletedata", cancellationToken)
            .ConfigureAwait(false);

        return JqlReader.ReadCatalogue(document.RootElement);
    }

    /// <summary>
    /// The values one field enumerates, narrowed by <paramref name="startsWith"/> where the caller
    /// gave one. Jira answers 200 with an empty list both for a field it does not know and for one
    /// that enumerates nothing, so there is no error here to map.
    /// </summary>
    public async Task<JiraJqlSuggestions> GetJqlSuggestionsAsync(
        string field,
        string? startsWith,
        CancellationToken cancellationToken)
    {
        var path = "rest/api/2/jql/autocompletedata/suggestions"
                   + $"?fieldName={Uri.EscapeDataString(field)}"
                   + (startsWith is { Length: > 0 } value
                       ? $"&fieldValue={Uri.EscapeDataString(value)}"
                       : "");

        using var document = await JsonAsync(path, cancellationToken).ConfigureAwait(false);

        return JqlReader.ReadSuggestions(field, document.RootElement);
    }

    /// <summary>
    /// The saved filters the account this token belongs to has favourited. Jira answers with a
    /// bare array and no paging envelope — the endpoint is unpaged, so any bound on the count is
    /// the caller's — and each row already carries its JQL without an <c>expand</c>.
    /// </summary>
    /// <remarks>
    /// Favourites rather than every filter this account can see: <c>/rest/api/2/filter/search</c>,
    /// which would answer the wider question, is Cloud-only and answers 404 on this project's Jira
    /// floor.
    /// </remarks>
    public Task<IReadOnlyList<JiraSavedFilter>> ListFavouriteFiltersAsync(
        CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<JiraSavedFilter>>("rest/api/2/filter/favourite", cancellationToken);

    /// <summary>
    /// A read whose payload is walked rather than deserialized, because what a caller needs from it
    /// is not the shape a serializer would build.
    /// </summary>
    private async Task<JsonDocument> JsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException($"Jira returned an empty body for /{path}.");
    }
}
