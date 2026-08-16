using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The two issue writes against an HTTP double: the body Jira is sent, what comes back, and — the
/// point of the whole resilience design — that neither is ever sent twice.
/// </summary>
public sealed class JiraWriteTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string CreatedPayload = """
        { "id": "10500", "key": "PROJ-42", "self": "https://jira.example.com/rest/api/2/issue/10500" }
        """;

    private readonly WireMockServer _jira = WireMockServer.Start();
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _jira.Stop();
    }

    [Fact]
    public async Task Creating_an_issue_returns_the_key_and_the_identifier_jira_assigned()
    {
        StubCreate(201, CreatedPayload);

        var created = await CreateClient().CreateIssueAsync(
            "PROJ",
            "Bug",
            "The login page returns 500",
            Fields(),
            TestContext.Current.CancellationToken);

        created.Key.ShouldBe("PROJ-42");
        created.Id.ShouldBe("10500");
    }

    [Fact]
    public async Task Creating_an_issue_names_the_project_the_type_and_the_summary_the_way_jira_wants()
    {
        StubCreate(201, CreatedPayload);

        await CreateClient().CreateIssueAsync(
            "PROJ",
            "Bug",
            "The login page returns 500",
            Fields(),
            TestContext.Current.CancellationToken);

        var request = SingleRequest();

        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/rest/api/2/issue");

        var fields = Body(request).GetProperty("fields");

        fields.GetProperty("project").GetProperty("key").GetString().ShouldBe("PROJ");
        fields.GetProperty("issuetype").GetProperty("name").GetString().ShouldBe("Bug");
        fields.GetProperty("summary").GetString().ShouldBe("The login page returns 500");
    }

    [Fact]
    public async Task A_custom_field_reaches_jira_under_its_own_identifier_and_shape()
    {
        StubCreate(201, CreatedPayload);

        await CreateClient().CreateIssueAsync(
            "PROJ",
            "Bug",
            "The login page returns 500",
            Fields(
                ("description", "\"It has done it twice today.\""),
                ("customfield_10010", """{ "id": "10300" }"""),
                ("labels", """[ "regression", "login" ]""")),
            TestContext.Current.CancellationToken);

        var fields = Body(SingleRequest()).GetProperty("fields");

        fields.GetProperty("description").GetString().ShouldBe("It has done it twice today.");
        fields.GetProperty("customfield_10010").GetProperty("id").GetString().ShouldBe("10300");
        fields.GetProperty("labels").EnumerateArray().Select(label => label.GetString())
            .ShouldBe(["regression", "login"]);
    }

    [Fact]
    public async Task Jiras_per_field_errors_on_a_rejected_create_arrive_intact()
    {
        StubCreate(400, """
            {
              "errorMessages": [],
              "errors": {
                "customfield_10010": "Team is required.",
                "duedate": "Date value 'tomorrow' is invalid."
              }
            }
            """);

        var refusal = await Should.ThrowAsync<JiraApiException>(
            () => CreateClient().CreateIssueAsync(
                "PROJ",
                "Bug",
                "The login page returns 500",
                Fields(),
                TestContext.Current.CancellationToken));

        refusal.FieldErrors["customfield_10010"].ShouldBe("Team is required.");
        refusal.FieldErrors["duedate"].ShouldBe("Date value 'tomorrow' is invalid.");
    }

    [Fact]
    public async Task A_create_jira_could_not_answer_is_never_sent_twice()
    {
        StubCreate(503, "");

        await Should.ThrowAsync<JiraApiException>(
            () => CreateClient().CreateIssueAsync(
                "PROJ",
                "Bug",
                "The login page returns 500",
                Fields(),
                TestContext.Current.CancellationToken));

        // A retried create is a duplicate issue, which is exactly the failure this project is
        // trying to avoid.
        ReceivedRequests().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Updating_an_issue_puts_the_named_fields_and_nothing_else()
    {
        StubUpdate("/rest/api/2/issue/PROJ-42", 204);

        await CreateClient().UpdateIssueAsync(
            "PROJ-42",
            Fields(("summary", "\"A better summary\"")),
            assignee: null,
            TestContext.Current.CancellationToken);

        var request = SingleRequest();

        request.Method.ShouldBe("PUT");
        request.Path.ShouldBe("/rest/api/2/issue/PROJ-42");

        var fields = Body(request).GetProperty("fields");

        fields.GetProperty("summary").GetString().ShouldBe("A better summary");
        fields.TryGetProperty("assignee", out _).ShouldBeFalse();
        fields.EnumerateObject().Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_field_set_to_null_reaches_jira_as_null_and_clears_it()
    {
        StubUpdate("/rest/api/2/issue/PROJ-42", 204);

        await CreateClient().UpdateIssueAsync(
            "PROJ-42",
            Fields(("duedate", "null"), ("summary", "\"A better summary\"")),
            assignee: null,
            TestContext.Current.CancellationToken);

        var fields = Body(SingleRequest()).GetProperty("fields");

        // Set to empty and not mentioned have to be different things, or a field can never be
        // cleared.
        fields.GetProperty("duedate").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_assignee_is_sent_in_the_same_call_as_the_fields()
    {
        StubUpdate("/rest/api/2/issue/PROJ-42", 204);

        await CreateClient().UpdateIssueAsync(
            "PROJ-42",
            Fields(("summary", "\"A better summary\"")),
            new JiraAssignee("jbloggs"),
            TestContext.Current.CancellationToken);

        var fields = Body(SingleRequest()).GetProperty("fields");

        // Jira Server keys users by name, not by Cloud's account identifier.
        fields.GetProperty("assignee").GetProperty("name").GetString().ShouldBe("jbloggs");
        fields.GetProperty("summary").GetString().ShouldBe("A better summary");
    }

    [Fact]
    public async Task An_assignee_of_nobody_unassigns_the_issue()
    {
        StubUpdate("/rest/api/2/issue/PROJ-42", 204);

        await CreateClient().UpdateIssueAsync(
            "PROJ-42",
            Fields(),
            new JiraAssignee(null),
            TestContext.Current.CancellationToken);

        Body(SingleRequest()).GetProperty("fields").GetProperty("assignee")
            .ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_update_jira_could_not_answer_is_never_sent_twice()
    {
        StubUpdate("/rest/api/2/issue/PROJ-42", 503);

        await Should.ThrowAsync<JiraApiException>(
            () => CreateClient().UpdateIssueAsync(
                "PROJ-42",
                Fields(("summary", "\"A better summary\"")),
                assignee: null,
                TestContext.Current.CancellationToken));

        ReceivedRequests().Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_issue_key_needing_escaping_is_escaped()
    {
        StubUpdate("/rest/api/2/issue/*", 204);

        await CreateClient().UpdateIssueAsync(
            "PROJ 42/../admin",
            Fields(("summary", "\"A better summary\"")),
            assignee: null,
            TestContext.Current.CancellationToken);

        // Escaped on the wire, so a key carrying path segments cannot walk out of the issue
        // endpoint and address something else.
        SingleRequest().Url.ShouldEndWith("/rest/api/2/issue/PROJ 42%2F..%2Fadmin");
    }

    private static IReadOnlyDictionary<string, JsonElement> Fields(
        params (string Name, string Json)[] fields) =>
        fields.ToDictionary(
            field => field.Name,
            field => JsonDocument.Parse(field.Json).RootElement,
            StringComparer.Ordinal);

    private static JsonElement Body(IRequestMessage request) =>
        JsonDocument.Parse(request.Body ?? string.Empty).RootElement;

    private void StubCreate(int status, string payload) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status)
                .WithHeader("Content-Type", "application/json")
                .WithBody(payload));

    private void StubUpdate(string path, int status) =>
        _jira.Given(Request.Create().WithPath(path).UsingPut())
            .RespondWith(Response.Create().WithStatusCode(status));

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private IReadOnlyList<WireMock.Logging.ILogEntry> ReceivedRequests() => [.. _jira.LogEntries];

    private JiraClient CreateClient()
    {
        var services = new ServiceCollection();

        services.AddJiraClient();
        services.Configure<JiraClientOptions>(options =>
        {
            options.BaseUrl = new Uri(_jira.Url!, UriKind.Absolute);
            options.PersonalAccessToken = Token;
        });

        var provider = services.BuildServiceProvider();

        _providers.Add(provider);

        return provider.GetRequiredService<JiraClient>();
    }
}
