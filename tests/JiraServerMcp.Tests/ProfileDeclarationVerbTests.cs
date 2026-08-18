using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// The alias and query verbs at the command line: exit codes, what an operator is told, and what
/// ends up on the profile. Both features were proven at the protocol seam, which says what an
/// agent sees but nothing about what the human declaring them sees when they get it wrong.
/// </summary>
public sealed class ProfileDeclarationVerbTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private readonly ConfigurationHome _home = new();

    private readonly WireMockServer _jira = WireMockServer.Start();

    public ProfileDeclarationVerbTests()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"name":"ada","displayName":"Ada Lovelace","active":true}"""));

        _jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"startAt":0,"maxResults":1,"total":0,"issues":[]}"""));
    }

    public void Dispose()
    {
        _jira.Stop();
        _home.Dispose();
    }

    [Fact]
    public async Task An_alias_is_declared_listed_and_removed()
    {
        await ReadyAsync();

        var set = await RunAsync(
            ["profile", "alias", "set", "work", "story_points", "customfield_10010"]);

        set.ExitCode.ShouldBe(0);
        set.StandardOutput.ShouldContain("customfield_10010");

        var listed = await RunAsync(["profile", "alias", "list", "work"]);

        listed.ExitCode.ShouldBe(0);
        listed.StandardOutput.ShouldContain("story_points -> customfield_10010");

        var removed = await RunAsync(["profile", "alias", "remove", "work", "story_points"]);

        removed.ExitCode.ShouldBe(0);

        (await RunAsync(["profile", "alias", "list", "work"]))
            .StandardOutput.ShouldContain("declares no field aliases");
    }

    [Fact]
    public async Task An_alias_spelled_like_a_field_identifier_is_refused_with_the_reason()
    {
        await ReadyAsync();

        var set = await RunAsync(
            ["profile", "alias", "set", "work", "customfield_10010", "customfield_10011"]);

        set.ExitCode.ShouldBe(1);
        set.StandardError.ShouldContain("spelled like a Jira field identifier");
        set.StandardError.ShouldNotContain("Unhandled exception");
    }

    [Fact]
    public async Task Removing_an_alias_that_was_never_declared_says_where_to_look()
    {
        await ReadyAsync();

        var removed = await RunAsync(["profile", "alias", "remove", "work", "nothing"]);

        removed.ExitCode.ShouldBe(1);
        removed.StandardError.ShouldContain("profile alias list work");
    }

    [Fact]
    public async Task A_query_is_declared_listed_and_removed()
    {
        await ReadyAsync();

        var added = await AddQueryAsync("sprint_bugs", "type = Bug", "This team's open bugs.");

        added.ExitCode.ShouldBe(0);
        added.StandardOutput.ShouldContain("jira_q_sprint_bugs");

        var listed = await RunAsync(["profile", "query", "list", "work"]);

        listed.StandardOutput.ShouldContain("jira_q_sprint_bugs: This team's open bugs.");
        listed.StandardOutput.ShouldContain("type = Bug");

        var removed = await RunAsync(["profile", "query", "remove", "work", "sprint_bugs"]);

        removed.ExitCode.ShouldBe(0);

        (await RunAsync(["profile", "query", "list", "work"]))
            .StandardOutput.ShouldContain("declares no queries");
    }

    [Fact]
    public async Task A_query_name_the_tool_name_could_not_carry_is_refused()
    {
        await ReadyAsync();

        var added = await AddQueryAsync("Sprint Bugs", "type = Bug", "Bad name.");

        added.ExitCode.ShouldBe(1);
        added.StandardError.ShouldContain("cannot name a query");

        // The name it would have become is named, so the operator can see why.
        added.StandardError.ShouldContain("jira_q_Sprint Bugs");
    }

    [Fact]
    public async Task A_query_declared_before_a_token_is_stored_says_which_verb_stores_one()
    {
        // The JQL is run against Jira before it is stored, so there is nothing to run it as.
        (await RunAsync(["profile", "add", "work", "--url", _jira.Url!])).ExitCode.ShouldBe(0);

        var added = await AddQueryAsync("sprint_bugs", "type = Bug", "This team's open bugs.");

        added.ExitCode.ShouldBe(1);
        added.StandardError.ShouldContain("auth login work");
    }

    [Fact]
    public async Task Declaring_a_query_twice_replaces_it_rather_than_adding_a_second()
    {
        await ReadyAsync();

        (await AddQueryAsync("sprint_bugs", "type = Bug", "First.")).ExitCode.ShouldBe(0);
        (await AddQueryAsync("sprint_bugs", "type = Story", "Second.")).ExitCode.ShouldBe(0);

        var listed = await RunAsync(["profile", "query", "list", "work"]);

        listed.StandardOutput.ShouldContain("Second.");
        listed.StandardOutput.ShouldNotContain("First.");
        listed.StandardOutput.ShouldNotContain("type = Bug");
    }

    private Task<HostProcessResult> AddQueryAsync(string name, string jql, string description) =>
        RunAsync([
            "profile", "query", "add", "work", name,
            "--jql", jql,
            "--description", description,
        ]);

    /// <summary>A profile with a token, which both verbs need before they will talk to Jira.</summary>
    private async Task ReadyAsync()
    {
        (await RunAsync(["profile", "add", "work", "--url", _jira.Url!])).ExitCode.ShouldBe(0);
        var loggedIn = await RunAsync(["auth", "login", "work"], Token + "\n");

        loggedIn.ExitCode.ShouldBe(0, loggedIn.StandardError);
    }

    private Task<HostProcessResult> RunAsync(string[] verb, string? standardInput = null) =>
        HostProcess.RunAsync(
            verb,
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput);
}
