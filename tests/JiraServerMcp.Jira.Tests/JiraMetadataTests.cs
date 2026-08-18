using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The metadata reads — projects, create fields, users — against an HTTP double: what Jira is
/// asked, and what comes back.
/// </summary>
public sealed class JiraMetadataTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string ProjectsPayload = """
        [
          {
            "id": "10000",
            "key": "PROJ",
            "name": "Platform",
            "projectTypeKey": "software"
          },
          {
            "id": "10001",
            "key": "OPS",
            "name": "Operations",
            "projectTypeKey": "business"
          }
        ]
        """;

    private const string ProjectPayload = """
        {
          "id": "10000",
          "key": "PROJ",
          "name": "Platform",
          "projectTypeKey": "software",
          "description": "The platform team's work",
          "lead": { "name": "ada", "displayName": "Ada Lovelace" }
        }
        """;

    private const string StatusesPayload = """
        [
          {
            "id": "10002",
            "name": "Bug",
            "subtask": false,
            "statuses": [
              { "id": "1", "name": "Open" },
              { "id": "3", "name": "In Progress" },
              { "id": "5", "name": "Resolved" }
            ]
          },
          {
            "id": "10003",
            "name": "Sub-task",
            "subtask": true,
            "statuses": [ { "id": "1", "name": "Open" } ]
          }
        ]
        """;

    private const string ComponentsPayload = """
        [
          { "id": "10100", "name": "api", "description": "The REST surface" },
          { "id": "10101", "name": "web" }
        ]
        """;

    private const string VersionsPayload = """
        [
          { "id": "10200", "name": "1.0.0", "released": true, "archived": false, "releaseDate": "2026-01-31" },
          { "id": "10201", "name": "1.1.0", "released": false, "archived": false }
        ]
        """;

    private const string CreateMetaPayload = """
        {
          "projects": [
            {
              "id": "10000",
              "key": "PROJ",
              "name": "Platform",
              "issuetypes": [
                {
                  "id": "10002",
                  "name": "Bug",
                  "fields": {
                    "summary": {
                      "required": true,
                      "name": "Summary",
                      "schema": { "type": "string", "system": "summary" }
                    },
                    "customfield_10010": {
                      "required": true,
                      "name": "Team",
                      "schema": { "type": "option", "custom": "com.atlassian.jira.plugin.system.customfieldtypes:select" },
                      "allowedValues": [
                        { "id": "10300", "value": "Platform" },
                        { "id": "10301", "value": "Operations" }
                      ]
                    },
                    "description": {
                      "required": false,
                      "name": "Description",
                      "schema": { "type": "string", "system": "description" }
                    }
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string UsersPayload = """
        [
          {
            "key": "JIRAUSER10100",
            "name": "ada",
            "displayName": "Ada Lovelace",
            "emailAddress": "ada@example.com",
            "active": true
          },
          {
            "key": "JIRAUSER10101",
            "name": "jbloggs",
            "displayName": "Joe Bloggs",
            "emailAddress": "jbloggs@example.com",
            "active": false
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
    public async Task Listing_projects_returns_the_key_name_id_and_type_of_each()
    {
        Stub("/rest/api/2/project", ProjectsPayload);

        var projects = await CreateClient().ListProjectsAsync(TestContext.Current.CancellationToken);

        projects.Count.ShouldBe(2);

        projects[0].Key.ShouldBe("PROJ");
        projects[0].Id.ShouldBe("10000");
        projects[0].Name.ShouldBe("Platform");
        projects[0].ProjectTypeKey.ShouldBe("software");

        projects[1].Key.ShouldBe("OPS");
        projects[1].ProjectTypeKey.ShouldBe("business");

        SingleRequest().Method.ShouldBe("GET");
    }

    [Fact]
    public async Task Reading_one_project_merges_the_project_its_statuses_components_and_versions()
    {
        StubProject();

        var project = await CreateClient().GetProjectAsync(
            "PROJ",
            TestContext.Current.CancellationToken);

        project.Project.Key.ShouldBe("PROJ");
        project.Project.Name.ShouldBe("Platform");
        project.Description.ShouldBe("The platform team's work");
        project.Lead.ShouldBe("Ada Lovelace");

        project.IssueTypes.Count.ShouldBe(2);
        project.IssueTypes[0].Name.ShouldBe("Bug");
        project.IssueTypes[0].Subtask.ShouldBeFalse();
        project.IssueTypes[0].Statuses.Select(status => status.Name)
            .ShouldBe(["Open", "In Progress", "Resolved"]);
        project.IssueTypes[1].Subtask.ShouldBeTrue();

        project.Components.Select(component => component.Name).ShouldBe(["api", "web"]);
        project.Components[0].Description.ShouldBe("The REST surface");

        project.Versions.Count.ShouldBe(2);
        project.Versions[0].Name.ShouldBe("1.0.0");
        project.Versions[0].Released.ShouldBeTrue();
        project.Versions[0].ReleaseDate.ShouldBe("2026-01-31");
        project.Versions[1].Released.ShouldBeFalse();
    }

    [Fact]
    public async Task Reading_one_project_asks_jira_for_all_four_parts()
    {
        StubProject();

        await CreateClient().GetProjectAsync("PROJ", TestContext.Current.CancellationToken);

        Paths().ShouldBe([
            "/rest/api/2/project/PROJ",
            "/rest/api/2/project/PROJ/statuses",
            "/rest/api/2/project/PROJ/components",
            "/rest/api/2/project/PROJ/versions",
        ], ignoreOrder: true);
    }

    [Fact]
    public async Task A_project_key_needing_escaping_is_escaped()
    {
        StubProject();
        Stub("/rest/api/2/project/A B", ProjectPayload);
        Stub("/rest/api/2/project/A B/statuses", StatusesPayload);
        Stub("/rest/api/2/project/A B/components", ComponentsPayload);
        Stub("/rest/api/2/project/A B/versions", VersionsPayload);

        await CreateClient().GetProjectAsync("A B", TestContext.Current.CancellationToken);

        Paths().ShouldContain("/rest/api/2/project/A B");
    }

    [Fact]
    public async Task Create_fields_carry_the_identifier_requiredness_type_and_allowed_values()
    {
        StubCreateMeta(CreateMetaPayload);

        var fields = (await CreateClient().GetCreateFieldsAsync(
            "PROJ",
            "Bug",
            TestContext.Current.CancellationToken)).ShouldNotBeNull();

        fields.ProjectKey.ShouldBe("PROJ");
        fields.IssueTypeName.ShouldBe("Bug");

        var custom = fields.Fields.Single(field => field.Id is "customfield_10010");

        custom.Name.ShouldBe("Team");
        custom.Required.ShouldBeTrue();
        custom.Type.ShouldBe("option");
        custom.AllowedValues.ShouldBe(["Platform", "Operations"]);

        var summary = fields.Fields.Single(field => field.Id is "summary");

        summary.Required.ShouldBeTrue();
        summary.Type.ShouldBe("string");
        summary.AllowedValues.ShouldBeEmpty();

        fields.Fields.Single(field => field.Id is "description").Required.ShouldBeFalse();
    }

    [Fact]
    public async Task The_create_metadata_request_names_the_project_the_type_and_the_expansion()
    {
        StubCreateMeta(CreateMetaPayload);

        await CreateClient().GetCreateFieldsAsync(
            "PROJ",
            "Bug",
            TestContext.Current.CancellationToken);

        var request = SingleRequest();

        request.Path.ShouldBe("/rest/api/2/issue/createmeta");

        var query = request.Query.ShouldNotBeNull();

        query["projectKeys"].ShouldHaveSingleItem().ShouldBe("PROJ");
        query["issuetypeNames"].ShouldHaveSingleItem().ShouldBe("Bug");
        query["expand"].ShouldHaveSingleItem().ShouldBe("projects.issuetypes.fields");
    }

    [Fact]
    public async Task A_project_or_type_jira_does_not_know_comes_back_as_nothing_rather_than_empty_fields()
    {
        StubCreateMeta("""{ "expand": "projects", "projects": [] }""");

        var fields = await CreateClient().GetCreateFieldsAsync(
            "NOPE",
            "Bug",
            TestContext.Current.CancellationToken);

        fields.ShouldBeNull();
    }

    [Fact]
    public async Task A_user_search_returns_usernames_display_names_emails_and_the_active_flag()
    {
        Stub("/rest/api/2/user/search", UsersPayload);

        var users = await SearchUsersAsync("ro", includeInactive: true);

        users.Count.ShouldBe(2);

        users[0].Name.ShouldBe("ada");
        users[0].DisplayName.ShouldBe("Ada Lovelace");
        users[0].EmailAddress.ShouldBe("ada@example.com");
        users[0].Active.ShouldBeTrue();

        users[1].Name.ShouldBe("jbloggs");
        users[1].Active.ShouldBeFalse();
    }

    [Fact]
    public async Task A_user_search_names_the_query_the_page_and_what_it_wants_of_inactive_users()
    {
        Stub("/rest/api/2/user/search", UsersPayload);

        await SearchUsersAsync("ro", startAt: 10, maxResults: 5, includeInactive: true);

        var query = SingleRequest().Query.ShouldNotBeNull();

        query["username"].ShouldHaveSingleItem().ShouldBe("ro");
        query["startAt"].ShouldHaveSingleItem().ShouldBe("10");
        query["maxResults"].ShouldHaveSingleItem().ShouldBe("5");
        query["includeInactive"].ShouldHaveSingleItem().ShouldBe("true");
    }

    [Fact]
    public async Task Inactive_users_are_left_out_unless_they_were_asked_for()
    {
        Stub("/rest/api/2/user/search", UsersPayload);

        await SearchUsersAsync("ro");

        SingleRequest().Query.ShouldNotBeNull()["includeInactive"]
            .ShouldHaveSingleItem().ShouldBe("false");
    }

    private Task<IReadOnlyList<Models.JiraUser>> SearchUsersAsync(
        string query,
        int startAt = 0,
        int maxResults = 25,
        bool includeInactive = false) =>
        CreateClient().SearchUsersAsync(
            query,
            startAt,
            maxResults,
            includeInactive,
            TestContext.Current.CancellationToken);

    private void StubProject()
    {
        Stub("/rest/api/2/project/PROJ", ProjectPayload);
        Stub("/rest/api/2/project/PROJ/statuses", StatusesPayload);
        Stub("/rest/api/2/project/PROJ/components", ComponentsPayload);
        Stub("/rest/api/2/project/PROJ/versions", VersionsPayload);
    }

    private void StubCreateMeta(string payload) => Stub("/rest/api/2/issue/createmeta", payload);

    private void Stub(string path, string payload) =>
        _jira.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(payload));

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private string[] Paths() =>
    [
        .. _jira.LogEntries.Select(entry => entry.RequestMessage?.Path).OfType<string>(),
    ];

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
