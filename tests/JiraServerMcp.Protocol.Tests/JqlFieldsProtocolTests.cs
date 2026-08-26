using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock.RequestBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The query catalogue across the protocol seam (ADR-0008): what an agent is shown of what this
/// Jira will accept in a query, and what a rejected query is pointed at.
/// </summary>
public sealed class JqlFieldsProtocolTests : IAsyncLifetime
{
    /// <summary>
    /// Autocomplete data shaped as Jira Server 8.20.7 sends it: booleans as strings, an absent
    /// <c>orderable</c> where a field may not be sorted on, a custom field's identifier in the
    /// bracket form, and its value pre-quoted because the quotes are part of the clause.
    /// </summary>
    private const string CataloguePayload = """
        {
          "visibleFieldNames": [
            {
              "value": "summary",
              "displayName": "summary",
              "orderable": "true",
              "searchable": "true",
              "operators": [ "~", "!~", "is", "is not" ],
              "types": [ "java.lang.String" ]
            },
            {
              "value": "attachments",
              "displayName": "attachments",
              "searchable": "true",
              "operators": [ "is", "is not" ],
              "types": [ "com.atlassian.jira.issue.attachment.Attachment" ]
            },
            {
              "value": "\"Story Points\"",
              "displayName": "Story Points - cf[10010]",
              "orderable": "true",
              "searchable": "true",
              "cfid": "cf[10010]",
              "operators": [ "=", "!=", "in", "not in" ],
              "types": [ "java.lang.Number" ]
            }
          ],
          "visibleFunctionNames": [
            {
              "value": "currentUser()",
              "displayName": "currentUser()",
              "types": [ "com.atlassian.jira.user.ApplicationUser" ]
            }
          ],
          "jqlReservedWords": [ "and", "or", "not" ]
        }
        """;

    private ProtocolSeam _seam = null!;

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _seam = await ProtocolSeam.StartAsync();

        await _seam.RunAsync(
            ["profile", "alias", "set", ProtocolSeam.Profile, "story_points", "customfield_10010"]);

        _client = await _seam.ConnectAsync();
    }

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task The_catalogue_is_registered_for_every_client_and_says_it_only_reads()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // No grant: grants are write categories and this is a read. No capability gate either —
        // the endpoint predates this project's supported Jira floor.
        var tool = tools.Single(candidate => candidate.Name is "jira_get_jql_fields");

        tool.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);

        tool.JsonSchema.TryGetProperty("required", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task The_catalogue_publishes_the_names_a_clause_may_use_and_what_each_takes()
    {
        StubCatalogue();

        var text = await CallAsync([]);

        // summary takes ~ and not =, and a field with no orderable may not be sorted on: both are
        // per-field facts an agent otherwise learns from a 400.
        text.ShouldContain("  summary  String  ~, !~, is, is not");
        text.ShouldContain("  attachments  Attachment  is, is not; not sortable");

        text.ShouldContain("functions (1)");
        text.ShouldContain("  currentUser()  ApplicationUser");
    }

    [Fact]
    public async Task A_custom_field_carries_its_jql_names_and_the_alias_declared_for_it()
    {
        StubCatalogue();

        // The alias resolves to customfield_10010, which appears nowhere in Jira's payload — the
        // join is on the number Jira publishes inside the bracket form.
        (await CallAsync([]))
            .ShouldContain("""  "Story Points"  cf[10010]  story_points (customfield_10010)  Number""");
    }

    [Fact]
    public async Task A_substring_narrows_the_catalogue_without_a_second_round_trip()
    {
        StubCatalogue();

        var text = await CallAsync([("startsWith", "story")]);

        text.ShouldContain("fields (1 of 3 matching 'story')");
        text.ShouldNotContain("  summary  ");
    }

    [Fact]
    public async Task Naming_a_field_asks_jira_what_that_field_accepts()
    {
        _seam.Jira.Given(Request.Create()
                .WithPath("/rest/api/2/jql/autocompletedata/suggestions").UsingGet())
            .RespondWith(JiraResponse.Json(200, """
                {"results":[{"value":"Open","displayName":"Open"},
                            {"value":"\"In Progress\"","displayName":"In Progress"}]}
                """));

        var result = await _client.CallToolAsync(
            "jira_get_jql_fields",
            new Dictionary<string, object?> { ["field"] = "status" },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true, TextOf(result));

        // The quoted form is what parses, so it is what is published.
        TextOf(result).ShouldContain("\"In Progress\"");

        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("field").GetString().ShouldBe("status");
    }

    [Fact]
    public async Task A_field_jira_publishes_nothing_for_is_refused_with_both_readings_named()
    {
        // Jira answers 200 with an empty list whether the name is unknown or the field simply
        // enumerates nothing, so silence is the failure mode and the tool says what Jira will not.
        _seam.Jira.Given(Request.Create()
                .WithPath("/rest/api/2/jql/autocompletedata/suggestions").UsingGet())
            .RespondWith(JiraResponse.Json(200, """{"results":[]}"""));

        var result = await _client.CallToolAsync(
            "jira_get_jql_fields",
            new Dictionary<string, object?> { ["field"] = "notafield" },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var text = TextOf(result);

        text.ShouldContain("may not be queryable under that name");
        text.ShouldContain("enumerate nothing");

        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("outcome").GetString().ShouldBe("refused");
    }

    [Fact]
    public async Task A_rejected_query_is_pointed_at_the_catalogue_and_at_the_name_it_must_use()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(JiraResponse.Json(400, """
                {"errorMessages":["Field 'customfield_10010' does not exist or you do not have permission to view it."],"errors":{}}
                """));

        var result = await _client.CallToolAsync(
            "jira_search",
            new Dictionary<string, object?> { ["jql"] = "customfield_10010 = 5" },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var text = TextOf(result);

        // The routine failure of this tool, and the only advice that closes the retry loop: the
        // names this Jira is queryable under, and the fact that the identifier is not one of them.
        text.ShouldContain("jira_get_jql_fields");
        text.ShouldContain("cf[NNNNN]");
        text.ShouldContain("story_points -> customfield_10010");
    }

    private async Task<string> CallAsync((string Name, object? Value)[] arguments)
    {
        var result = await _client.CallToolAsync(
            "jira_get_jql_fields",
            arguments.ToDictionary(argument => argument.Name, argument => argument.Value),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true, TextOf(result));

        return TextOf(result);
    }

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

    private void StubCatalogue() =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/jql/autocompletedata").UsingGet())
            .RespondWith(JiraResponse.Json(200, CataloguePayload));
}
