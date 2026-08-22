using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The JSON answer Jira gives, as the double has to spell it. Building one has nothing to do with
/// the seam, so it lives beside <see cref="ProtocolSeam"/> rather than on it.
/// </summary>
internal static class JiraResponse
{
    public static IResponseBuilder Json(int status, string body) =>
        Response.Create().WithStatusCode(status)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);
}
