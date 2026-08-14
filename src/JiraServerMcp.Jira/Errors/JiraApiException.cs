using System.Net;

namespace JiraServerMcp.Jira.Errors;

/// <summary>
/// A Jira response that was not a success. It carries the status, the endpoint that produced it,
/// Jira's <c>errorMessages</c> list and its per-field <c>errors</c> map — and nothing else. The
/// request message is deliberately absent: it holds the personal access token.
/// </summary>
public sealed class JiraApiException(
    HttpStatusCode statusCode,
    string endpoint,
    IReadOnlyList<string> errorMessages,
    IReadOnlyDictionary<string, string> fieldErrors)
    : Exception(Describe(statusCode, errorMessages, fieldErrors))
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>The path Jira answered on, such as <c>/rest/api/2/issue/ABC-1</c>.</summary>
    public string Endpoint { get; } = endpoint;

    public IReadOnlyList<string> ErrorMessages { get; } = errorMessages;

    /// <summary>
    /// Jira's per-field validation errors, keyed by field id. Empty for everything but a rejected
    /// write.
    /// </summary>
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors;

    private static string Describe(
        HttpStatusCode statusCode,
        IReadOnlyList<string> errorMessages,
        IReadOnlyDictionary<string, string> fieldErrors)
    {
        var described = new List<string> { $"Jira returned {(int)statusCode} {statusCode}." };

        described.AddRange(errorMessages);
        described.AddRange(fieldErrors.Select(field => $"{field.Key}: {field.Value}"));

        return string.Join(" ", described);
    }
}
