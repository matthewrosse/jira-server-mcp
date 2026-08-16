using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// Transitioning against an HTTP double: what Jira is asked for the transitions available now, the
/// body one transition carries, and that the transition itself is never sent twice.
/// </summary>
public sealed class JiraTransitionTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string TransitionsPayload = """
        {
          "transitions": [
            {
              "id": "21",
              "name": "Start Progress",
              "to": { "name": "In Progress" },
              "fields": {}
            },
            {
              "id": "31",
              "name": "Done",
              "to": { "name": "Done" },
              "fields": {
                "resolution": { "name": "Resolution", "required": true }
              }
            }
          ]
        }
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
    public async Task The_transitions_available_now_come_back_named_and_numbered()
    {
        StubTransitions(200, TransitionsPayload);

        var transitions = await CreateClient()
            .ListTransitionsAsync("PROJ-42", TestContext.Current.CancellationToken);

        transitions.Select(transition => transition.Name).ShouldBe(["Start Progress", "Done"]);
        transitions[1].Id.ShouldBe("31");
        transitions[1].ToStatus.ShouldBe("Done");
    }

    [Fact]
    public async Task The_transition_screens_are_not_asked_for()
    {
        StubTransitions(200, TransitionsPayload);

        await CreateClient().ListTransitionsAsync("PROJ-42", TestContext.Current.CancellationToken);

        var request = SingleRequest();

        request.Method.ShouldBe("GET");
        request.Path.ShouldBe("/rest/api/2/issue/PROJ-42/transitions");

        // The screens are the largest part of that response, and resolving a name needs none of
        // them. An agent that wants them asks jira_get_issue for the transitions expansion.
        request.Url.ShouldNotContain("expand");
    }

    [Fact]
    public async Task A_transition_names_the_identifier_jira_gave_it()
    {
        StubTransition(204);

        await CreateClient().TransitionIssueAsync(
            "PROJ-42",
            "31",
            Fields(),
            comment: null,
            TestContext.Current.CancellationToken);

        var request = SingleRequest();

        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/rest/api/2/issue/PROJ-42/transitions");

        Body(request).GetProperty("transition").GetProperty("id").GetString().ShouldBe("31");
    }

    [Fact]
    public async Task A_comment_and_the_screen_fields_ride_along_in_the_same_request()
    {
        StubTransition(204);

        await CreateClient().TransitionIssueAsync(
            "PROJ-42",
            "31",
            Fields(("resolution", """{ "name": "Fixed" }""")),
            "Fixed in the release branch.",
            TestContext.Current.CancellationToken);

        // One request, because a transition demanding a resolution must succeed in one call.
        var body = Body(SingleRequest());

        body.GetProperty("fields").GetProperty("resolution").GetProperty("name").GetString()
            .ShouldBe("Fixed");

        body.GetProperty("update").GetProperty("comment")
            .EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("add").GetProperty("body").GetString()
            .ShouldBe("Fixed in the release branch.");
    }

    [Fact]
    public async Task A_transition_carrying_neither_sends_neither()
    {
        StubTransition(204);

        await CreateClient().TransitionIssueAsync(
            "PROJ-42",
            "31",
            Fields(),
            comment: null,
            TestContext.Current.CancellationToken);

        var body = Body(SingleRequest());

        body.TryGetProperty("fields", out _).ShouldBeFalse();
        body.TryGetProperty("update", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_transition_jira_could_not_answer_is_never_sent_twice()
    {
        StubTransition(503);

        await Should.ThrowAsync<JiraApiException>(
            () => CreateClient().TransitionIssueAsync(
                "PROJ-42",
                "31",
                Fields(),
                comment: null,
                TestContext.Current.CancellationToken));

        _jira.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task An_issue_key_needing_escaping_is_escaped()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/*").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        await CreateClient().TransitionIssueAsync(
            "PROJ 42/../admin",
            "31",
            Fields(),
            comment: null,
            TestContext.Current.CancellationToken);

        SingleRequest().Url.ShouldEndWith(
            "/rest/api/2/issue/PROJ 42%2F..%2Fadmin/transitions");
    }

    private static IReadOnlyDictionary<string, JsonElement> Fields(
        params (string Name, string Json)[] fields) =>
        fields.ToDictionary(
            field => field.Name,
            field => JsonDocument.Parse(field.Json).RootElement,
            StringComparer.Ordinal);

    private static JsonElement Body(IRequestMessage request) =>
        JsonDocument.Parse(request.Body ?? string.Empty).RootElement;

    private void StubTransitions(int status, string payload) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/transitions").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status)
                .WithHeader("Content-Type", "application/json")
                .WithBody(payload));

    private void StubTransition(int status) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/transitions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status));

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

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
