using System.Net;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The query catalogue read, and the saved filters beside it, against the payloads a real Jira
/// Server 8.20.7 sent: what is asked of Jira, and what the reader makes of the answer. The
/// fixtures are the point here — every
/// surprising thing this reader handles (a boolean sent as a string, a pre-quoted value, the
/// bracket form of a custom field's identifier) is a property of the real payload rather than of
/// a payload written to suit the reader.
/// </summary>
public sealed class JiraJqlTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    /// <summary>
    /// Two favourites as 8.20.7 sends them: the JQL is there without an expand, one row has no
    /// description, and most of the bytes are the owner's avatars and the sharing envelopes that
    /// no caller of this client ever sees.
    /// </summary>
    private const string FavouritesPayload = """
        [
          {
            "self": "http://jira.invalid/rest/api/2/filter/10001",
            "id": "10001",
            "name": "Open payment bugs",
            "description": "What the payments team triages every morning.",
            "owner": {
              "self": "http://jira.invalid/rest/api/2/user?username=grace",
              "name": "grace",
              "key": "grace",
              "displayName": "Grace Hopper",
              "active": true,
              "avatarUrls": {
                "48x48": "http://jira.invalid/secure/useravatar?avatarId=10122",
                "24x24": "http://jira.invalid/secure/useravatar?size=small&avatarId=10122"
              }
            },
            "jql": "project = PAY AND status != Done ORDER BY created DESC",
            "viewUrl": "http://jira.invalid/issues/?filter=10001",
            "searchUrl": "http://jira.invalid/rest/api/2/search?jql=filter%3D10001",
            "favourite": true,
            "editable": true,
            "sharePermissions": [ { "id": 10000, "type": "group", "group": { "name": "jira-users" } } ],
            "sharedUsers": { "size": 0, "items": [], "max-results": 1000, "start-index": 0, "end-index": 0 },
            "subscriptions": { "size": 0, "items": [], "max-results": 1000, "start-index": 0, "end-index": 0 }
          },
          {
            "self": "http://jira.invalid/rest/api/2/filter/10002",
            "id": "10002",
            "name": "Anything assigned to me",
            "owner": {
              "self": "http://jira.invalid/rest/api/2/user?username=ada",
              "name": "ada",
              "displayName": "Ada Lovelace",
              "active": true
            },
            "jql": "assignee = currentUser() ORDER BY updated DESC",
            "viewUrl": "http://jira.invalid/issues/?filter=10002",
            "favourite": true,
            "editable": false,
            "sharePermissions": [],
            "sharedUsers": { "size": 0, "items": [] },
            "subscriptions": { "size": 0, "items": [] }
          }
        ]
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
    public async Task The_catalogue_comes_back_with_every_field_and_function_jira_published()
    {
        Stub("/rest/api/2/jql/autocompletedata", Fixture("jql-autocompletedata.json"));

        var catalogue = await CreateClient().GetJqlFieldsAsync(TestContext.Current.CancellationToken);

        catalogue.Fields.Count.ShouldBe(70);
        catalogue.Functions.Count.ShouldBe(37);

        catalogue.Functions.Select(function => function.Name).ShouldContain("currentUser()");
    }

    [Fact]
    public async Task A_custom_field_carries_its_bracket_form_and_the_quotes_jira_published()
    {
        Stub("/rest/api/2/jql/autocompletedata", Fixture("jql-autocompletedata.json"));

        var catalogue = await CreateClient().GetJqlFieldsAsync(TestContext.Current.CancellationToken);

        var storyPoints = catalogue.Fields
            .Single(field => field.CustomFieldId is "cf[10107]");

        // The quotes are part of the clause: Jira publishes the quoted form because that is what
        // parses, and dequoting it here would publish something that does not.
        storyPoints.Name.ShouldBe("\"Story Points\"");

        storyPoints.Types.ShouldBe(["java.lang.Number"]);
        storyPoints.Operators.ShouldContain("<=");

        // customfield_10107 is nowhere in this payload, and a clause built from it is rejected.
        catalogue.Fields.ShouldNotContain(field => field.Name.Contains("customfield_"));
    }

    [Fact]
    public async Task A_flag_jira_sends_as_a_string_is_read_as_the_boolean_it_means()
    {
        Stub("/rest/api/2/jql/autocompletedata", Fixture("jql-autocompletedata.json"));

        var catalogue = await CreateClient().GetJqlFieldsAsync(TestContext.Current.CancellationToken);

        catalogue.Fields.Single(field => field.Name is "summary").Orderable.ShouldBeTrue();

        // Absent rather than "false": a field with no orderable is one an ORDER BY may not name.
        catalogue.Fields.Single(field => field.Name is "attachments").Orderable.ShouldBeFalse();

        catalogue.Fields.Count(field => !field.Orderable).ShouldBe(17);
        catalogue.Fields.Count(field => !field.Searchable).ShouldBe(2);
    }

    [Fact]
    public async Task A_fields_values_come_back_as_they_are_written_in_a_clause()
    {
        Stub(
            "/rest/api/2/jql/autocompletedata/suggestions",
            Fixture("jql-suggestions.json"));

        var suggestions = await CreateClient().GetJqlSuggestionsAsync(
            "status",
            startsWith: null,
            TestContext.Current.CancellationToken);

        suggestions.Field.ShouldBe("status");
        suggestions.Values.ShouldContain("\"In Progress\"");
        suggestions.Values.ShouldContain("Open");

        SingleQuery().ShouldBe("?fieldName=status");
    }

    [Fact]
    public async Task A_value_filter_is_asked_of_jira_rather_than_applied_here()
    {
        Stub("/rest/api/2/jql/autocompletedata/suggestions", """{ "results": [] }""");

        await CreateClient().GetJqlSuggestionsAsync(
            "status",
            startsWith: "In Pro",
            TestContext.Current.CancellationToken);

        SingleQuery().ShouldBe("?fieldName=status&fieldValue=In%20Pro");
    }

    [Fact]
    public async Task A_field_jira_knows_nothing_about_is_an_empty_answer_rather_than_an_error()
    {
        // Jira answers 200 with an empty list for a name it does not know, exactly as it does for
        // a real field that enumerates nothing. Telling the two apart is not this reader's job.
        Stub("/rest/api/2/jql/autocompletedata/suggestions", """{ "results": [] }""");

        var suggestions = await CreateClient().GetJqlSuggestionsAsync(
            "notafield",
            startsWith: null,
            TestContext.Current.CancellationToken);

        suggestions.Values.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_favourites_are_read_from_the_endpoint_that_needs_no_expand()
    {
        Stub("/rest/api/2/filter/favourite", FavouritesPayload);

        var filters = await CreateClient().ListFavouriteFiltersAsync(
            TestContext.Current.CancellationToken);

        filters.Count.ShouldBe(2);

        var request = _jira.LogEntries.ShouldHaveSingleItem().RequestMessage.ShouldNotBeNull();

        request.Path.ShouldBe("/rest/api/2/filter/favourite");

        // The JQL arrives without asking for it: no expand, and so no second call per filter.
        request.RawQuery.ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task A_filter_carries_its_query_and_its_owner_and_nothing_else_off_the_payload()
    {
        Stub("/rest/api/2/filter/favourite", FavouritesPayload);

        var filters = await CreateClient().ListFavouriteFiltersAsync(
            TestContext.Current.CancellationToken);

        var open = filters.Single(filter => filter.Id is "10001");

        open.Name.ShouldBe("Open payment bugs");
        open.Description.ShouldBe("What the payments team triages every morning.");
        open.Jql.ShouldBe("project = PAY AND status != Done ORDER BY created DESC");

        // A favourite can be somebody else's filter, which is the whole reason it stays current.
        open.Owner.ShouldNotBeNull().Name.ShouldBe("grace");

        // The projection is what the model declares: the avatar URLs, the share permissions, the
        // shared users and the subscriptions are most of the payload's bytes and reach no caller.
        JsonSerializer.Serialize(open).ShouldNotContain("avatar");
        JsonSerializer.Serialize(open).ShouldNotContain("sharePermissions");
    }

    [Fact]
    public async Task A_filter_its_owner_wrote_no_description_for_comes_back_without_one()
    {
        Stub("/rest/api/2/filter/favourite", FavouritesPayload);

        var filters = await CreateClient().ListFavouriteFiltersAsync(
            TestContext.Current.CancellationToken);

        filters.Single(filter => filter.Id is "10002").Description.ShouldBeNull();
    }

    [Fact]
    public async Task A_jira_without_the_favourites_endpoint_is_a_jira_api_failure()
    {
        // Not the 404 this project probed on /filter/search, but the same shape: an endpoint this
        // Jira does not serve is a failure the tool reports rather than an empty list.
        _jira.Given(Request.Create().WithPath("/rest/api/2/filter/favourite").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "errorMessages": ["The filter service is not available."], "errors": {} }"""));

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => CreateClient().ListFavouriteFiltersAsync(TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        exception.ErrorMessages.ShouldContain("The filter service is not available.");
    }

    private string SingleQuery() =>
        _jira.LogEntries.ShouldHaveSingleItem().RequestMessage.ShouldNotBeNull().RawQuery
            .ShouldNotBeNull();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName, "tests", "fixtures", "payloads", "8.20.7", name));

    private void Stub(string path, string payload) =>
        _jira.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(payload));

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
