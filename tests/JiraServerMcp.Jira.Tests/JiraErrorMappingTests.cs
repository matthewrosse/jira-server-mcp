using System.Net;
using System.Text;
using JiraServerMcp.Jira.Errors;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// Jira reports a failure as an <c>errorMessages</c> list and a per-field <c>errors</c> map. Both
/// halves survive the trip into the exception, because the field map is the one case where Jira's
/// own wording is worth more than anything this project could write.
/// </summary>
public sealed class JiraErrorMappingTests
{
    [Fact]
    public async Task A_success_passes_through_untouched()
    {
        using var response = Respond(HttpStatusCode.OK, """{"key":"ABC-1"}""");

        await JiraResponse.EnsureSuccessAsync(response, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_failure_carries_the_status_the_endpoint_and_jiras_messages()
    {
        using var response = Respond(
            HttpStatusCode.Unauthorized,
            """{"errorMessages":["You do not have permission."],"errors":{}}""");

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => JiraResponse.EnsureSuccessAsync(response, TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        exception.Endpoint.ShouldBe("/rest/api/2/myself");
        exception.ErrorMessages.ShouldContain("You do not have permission.");
    }

    [Fact]
    public async Task A_rejected_write_carries_jiras_per_field_errors_intact()
    {
        using var response = Respond(
            HttpStatusCode.BadRequest,
            """
            {
              "errorMessages": [],
              "errors": {
                "summary": "Summary is required.",
                "customfield_10100": "Sprint is not on the appropriate screen."
              }
            }
            """);

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => JiraResponse.EnsureSuccessAsync(response, TestContext.Current.CancellationToken));

        exception.FieldErrors["summary"].ShouldBe("Summary is required.");
        exception.FieldErrors["customfield_10100"]
            .ShouldBe("Sprint is not on the appropriate screen.");
        exception.Message.ShouldContain("summary: Summary is required.");
    }

    [Fact]
    public async Task A_failure_with_no_json_body_still_carries_the_status_code()
    {
        using var response = Respond(
            HttpStatusCode.ServiceUnavailable,
            "<html>down for maintenance</html>");

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => JiraResponse.EnsureSuccessAsync(response, TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        exception.ErrorMessages.ShouldBeEmpty();
        exception.FieldErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_redirect_is_an_error_naming_where_it_tried_to_send_us()
    {
        using var response = Respond(HttpStatusCode.Found, string.Empty);
        response.Headers.Location = new Uri("https://sso.example.com/login", UriKind.Absolute);

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => JiraResponse.EnsureSuccessAsync(response, TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.Found);
        exception.Message.ShouldContain("https://sso.example.com/login");
    }

    [Fact]
    public async Task A_redirect_with_no_location_still_fails()
    {
        using var response = Respond(HttpStatusCode.Found, string.Empty);

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => JiraResponse.EnsureSuccessAsync(response, TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    [Fact]
    public async Task The_exception_never_carries_the_request_message()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://jira.example.com/rest/api/2/issue");

        request.Headers.Add("Authorization", "Bearer s3cr3t-personal-access-token");

        using var response = Respond(HttpStatusCode.Forbidden, """{"errorMessages":["No."]}""");
        response.RequestMessage = request;

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => JiraResponse.EnsureSuccessAsync(response, TestContext.Current.CancellationToken));

        exception.ToString().ShouldNotContain("s3cr3t-personal-access-token");
        exception.Endpoint.ShouldBe("/rest/api/2/issue");
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://jira.example.com/rest/api/2/myself"),
        };
}
