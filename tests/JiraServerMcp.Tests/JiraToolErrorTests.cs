using System.Net;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira.Errors;

namespace JiraServerMcp.Tests;

/// <summary>
/// An agent cannot read this server's log, so a failed tool call has to say what to do about it
/// in the result itself.
/// </summary>
public sealed class JiraToolErrorTests
{
    [Fact]
    public void An_invalid_or_revoked_token_names_the_profile_and_the_login_command()
    {
        var message = Describe(HttpStatusCode.Unauthorized, "/rest/api/2/myself");

        message.ShouldContain("work");
        message.ShouldContain("jira-server-mcp auth login work");
    }

    [Fact]
    public void A_refusal_names_the_operation_and_the_missing_permission()
    {
        var message = Describe(HttpStatusCode.Forbidden, "/rest/api/2/issue/ABC-1");

        message.ShouldContain("jira_get_issue");
        message.ShouldContain("permission");
    }

    [Fact]
    public void A_missing_issue_explains_that_jira_answers_the_same_way_either_way()
    {
        var message = Describe(HttpStatusCode.NotFound, "/rest/api/2/issue/ABC-1");

        message.ShouldContain("does not exist");
        message.ShouldContain("cannot see it");
    }

    [Fact]
    public void A_404_that_is_not_an_issue_points_at_the_base_url_instead()
    {
        var message = Describe(HttpStatusCode.NotFound, "/rest/api/2/myself");

        message.ShouldContain("base URL");
        message.ShouldNotContain("cannot see it");
    }

    [Fact]
    public void A_rejected_write_carries_jiras_per_field_errors_verbatim()
    {
        var exception = new JiraApiException(
            HttpStatusCode.BadRequest,
            "/rest/api/2/issue",
            [],
            new Dictionary<string, string>
            {
                ["summary"] = "Summary is required.",
                ["customfield_10100"] = "Sprint is not on the appropriate screen.",
            });

        var message = JiraToolError.Describe(exception, "work", "jira_create_issue");

        message.ShouldContain("summary: Summary is required.");
        message.ShouldContain("customfield_10100: Sprint is not on the appropriate screen.");
    }

    [Fact]
    public void A_redirect_is_reported_with_where_it_tried_to_send_us()
    {
        var exception = new JiraApiException(
            HttpStatusCode.Found,
            "/rest/api/2/myself",
            ["Jira answered with a redirect to https://sso.example.com/login."],
            new Dictionary<string, string>());

        JiraToolError.Describe(exception, "work", "jira_whoami")
            .ShouldContain("https://sso.example.com/login");
    }

    [Fact]
    public void No_message_carries_the_personal_access_token()
    {
        var statuses = new[]
        {
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.NotFound,
            HttpStatusCode.BadRequest,
            HttpStatusCode.ServiceUnavailable,
        };

        foreach (var status in statuses)
        {
            Describe(status, "/rest/api/2/issue/ABC-1").ShouldNotContain("s3cr3t");
        }
    }

    private static string Describe(HttpStatusCode status, string endpoint) =>
        JiraToolError.Describe(
            new JiraApiException(status, endpoint, [], new Dictionary<string, string>()),
            "work",
            status is HttpStatusCode.Unauthorized ? "jira_whoami" : "jira_get_issue");
}
