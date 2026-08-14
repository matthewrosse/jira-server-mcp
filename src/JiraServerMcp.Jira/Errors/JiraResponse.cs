using System.Net.Http.Json;
using System.Text.Json;

namespace JiraServerMcp.Jira.Errors;

/// <summary>
/// Turns a Jira response that was not a success into a <see cref="JiraApiException"/>. Every call
/// in this client goes through here, so error shape is decided once.
/// </summary>
public static class JiraResponse
{
    /// <summary>
    /// Returns the response untouched when Jira succeeded; throws otherwise. A redirect counts as
    /// a failure: following one would replay the personal access token at whatever host Jira
    /// named, so the location is reported rather than visited.
    /// </summary>
    public static async Task<HttpResponseMessage> EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var endpoint = response.RequestMessage?.RequestUri?.AbsolutePath ?? "(unknown endpoint)";

        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new JiraApiException(
                response.StatusCode,
                endpoint,
                [DescribeRedirect(response)],
                new Dictionary<string, string>());
        }

        var (errorMessages, fieldErrors) =
            await ReadErrorsAsync(response, cancellationToken).ConfigureAwait(false);

        throw new JiraApiException(response.StatusCode, endpoint, errorMessages, fieldErrors);
    }

    private static string DescribeRedirect(HttpResponseMessage response)
    {
        var location = response.Headers.Location is { } uri
            ? $"to {uri}"
            : "with no Location header";

        return $"Jira answered with a redirect {location}. Redirects are not followed, because "
               + "that would replay the personal access token at another host.";
    }

    /// <summary>
    /// Jira reports failures as <c>errorMessages</c> and <c>errors</c>, but a proxy, a login
    /// redirect, or an outage page can answer instead, so an unreadable body is not itself an
    /// error.
    /// </summary>
    private static async Task<(IReadOnlyList<string>, IReadOnlyDictionary<string, string>)>
        ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var body = await response.Content
                .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                .ConfigureAwait(false);

            if (body is null || body.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return ([], new Dictionary<string, string>());
            }

            return (ReadMessages(body.RootElement), ReadFieldErrors(body.RootElement));
        }
        catch (JsonException)
        {
            return ([], new Dictionary<string, string>());
        }
        catch (NotSupportedException)
        {
            return ([], new Dictionary<string, string>());
        }
    }

    private static IReadOnlyList<string> ReadMessages(JsonElement root)
    {
        if (!root.TryGetProperty("errorMessages", out var messages)
            || messages.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return [.. messages.EnumerateArray().Select(message => message.GetString()).OfType<string>()];
    }

    private static IReadOnlyDictionary<string, string> ReadFieldErrors(JsonElement root)
    {
        var fieldErrors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!root.TryGetProperty("errors", out var errors)
            || errors.ValueKind is not JsonValueKind.Object)
        {
            return fieldErrors;
        }

        foreach (var field in errors.EnumerateObject())
        {
            // Jira's own wording, verbatim: it is the one message a caller can act on directly.
            if (field.Value.ValueKind is JsonValueKind.String
                && field.Value.GetString() is { } message)
            {
                fieldErrors[field.Name] = message;
            }
        }

        return fieldErrors;
    }
}
