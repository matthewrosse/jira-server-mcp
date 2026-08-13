using System.Diagnostics;
using System.Net;
using JiraServerMcp.Jira.Errors;
using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The Jira client exercised against an HTTP double, as ADR-0003 intends: no MCP concept
/// present, and no mocked <see cref="HttpMessageHandler"/> — the assertions are on the wire.
/// </summary>
public sealed class JiraClientTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string MyselfPayload = """
        {
          "self": "http://localhost/rest/api/2/user?username=mrosse",
          "key": "JIRAUSER10100",
          "name": "mrosse",
          "emailAddress": "mrosse@example.com",
          "displayName": "Mateusz Różański",
          "active": true,
          "deleted": false,
          "timeZone": "Europe/Warsaw",
          "locale": "en_US"
        }
        """;

    private readonly WireMockServer _jira = WireMockServer.Start();
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        // Each provider owns an IHttpClientFactory with a handler-expiry timer and a socket
        // handler, none of which the WireMock shutdown touches.
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _jira.Stop();
    }

    [Fact]
    public async Task Myself_carries_display_name_username_email_and_active_flag()
    {
        StubMyself(Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(MyselfPayload));

        var user = await CreateClient().GetMyselfAsync(TestContext.Current.CancellationToken);

        user.DisplayName.ShouldBe("Mateusz Różański");
        user.Name.ShouldBe("mrosse");
        user.EmailAddress.ShouldBe("mrosse@example.com");
        user.Active.ShouldBeTrue();
    }

    [Fact]
    public async Task Request_hits_the_platform_api_myself_endpoint()
    {
        StubMyself(Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(MyselfPayload));

        await CreateClient().GetMyselfAsync(TestContext.Current.CancellationToken);

        var request = SingleRequest();

        request.Path.ShouldBe("/rest/api/2/myself");
        request.Method.ShouldBe("GET");
    }

    [Fact]
    public async Task Request_carries_the_personal_access_token_as_a_bearer_header()
    {
        StubMyself(Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(MyselfPayload));

        await CreateClient().GetMyselfAsync(TestContext.Current.CancellationToken);

        var headers = SingleRequest().Headers.ShouldNotBeNull();

        headers["Authorization"].ShouldHaveSingleItem().ShouldBe("Bearer " + Token);
    }

    [Fact]
    public async Task Redirects_are_not_followed()
    {
        StubMyself(Response.Create().WithStatusCode(302).WithHeader("Location", "/moved"));
        _jira.Given(Request.Create().WithPath("/moved").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(MyselfPayload));

        var client = CreateClient();

        await Should.ThrowAsync<JiraApiException>(
            () => client.GetMyselfAsync(TestContext.Current.CancellationToken));

        _jira.LogEntries.Select(entry => entry?.RequestMessage?.Path).ShouldNotContain("/moved");
    }

    [Fact]
    public async Task A_rejected_token_carries_the_status_code_and_jiras_own_message()
    {
        StubMyself(Response.Create().WithStatusCode(401)
            .WithHeader("Content-Type", "application/json")
            .WithBody("""{"errorMessages":["You do not have permission."],"errors":{}}"""));

        var client = CreateClient();

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => client.GetMyselfAsync(TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        exception.ErrorMessages.ShouldContain("You do not have permission.");
    }

    [Fact]
    public async Task A_failure_with_no_json_body_still_carries_the_status_code()
    {
        StubMyself(Response.Create().WithStatusCode(503).WithBody("<html>down for maintenance</html>"));

        var client = CreateClient();

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => client.GetMyselfAsync(TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        exception.ErrorMessages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_the_call_aborts_the_in_flight_request()
    {
        StubMyself(Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(MyselfPayload)
            .WithDelay(TimeSpan.FromSeconds(10)));

        var client = CreateClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var stopwatch = Stopwatch.StartNew();

        await Should.ThrowAsync<OperationCanceledException>(
            () => client.GetMyselfAsync(cancellation.Token));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_context_path_in_the_base_url_is_kept()
    {
        _jira.Given(Request.Create().WithPath("/jira/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(MyselfPayload));

        var client = CreateClient(new Uri(_jira.Url + "/jira", UriKind.Absolute));

        await client.GetMyselfAsync(TestContext.Current.CancellationToken);

        SingleRequest().Path.ShouldBe("/jira/rest/api/2/myself");
    }

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private void StubMyself(IResponseBuilder response) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet()).RespondWith(response);

    private JiraClient CreateClient(Uri? baseUrl = null)
    {
        var services = new ServiceCollection();

        services.AddJiraClient();
        services.Configure<JiraClientOptions>(options =>
        {
            options.BaseUrl = baseUrl ?? new Uri(_jira.Url!, UriKind.Absolute);
            options.PersonalAccessToken = Token;
        });

        var provider = services.BuildServiceProvider();

        _providers.Add(provider);

        return provider.GetRequiredService<JiraClient>();
    }
}
