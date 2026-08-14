using System.Net;

namespace JiraServerMcp.Jira.Errors;

/// <summary>
/// A Jira response that was not a success. Jira reports failures as an <c>errorMessages</c>
/// array; the per-field <c>errors</c> map is read by the write tools in a later phase.
/// </summary>
public sealed class JiraApiException(HttpStatusCode statusCode, IReadOnlyList<string> errorMessages)
    : Exception(Describe(statusCode, errorMessages))
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public IReadOnlyList<string> ErrorMessages { get; } = errorMessages;

    private static string Describe(HttpStatusCode statusCode, IReadOnlyList<string> errorMessages)
    {
        var status = $"Jira returned {(int)statusCode} {statusCode}.";

        return errorMessages.Count is 0 ? status : $"{status} {string.Join(" ", errorMessages)}";
    }
}
