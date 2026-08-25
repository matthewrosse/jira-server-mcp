using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock.RequestBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The edit screen across the protocol seam (ADR-0008): what an agent is shown of one issue's own
/// vocabulary, and what a rejected update is pointed at.
/// </summary>
public sealed class EditFieldsProtocolTests : IAsyncLifetime
{
    /// <summary>
    /// One issue's edit screen as Jira Server 8.20.7 sends it: a bare <c>fields</c> object, and
    /// <c>operations</c> on every field — including the ones no write may touch.
    /// </summary>
    private const string EditMetaPayload = """
        {
          "fields": {
            "summary": {
              "required": true,
              "name": "Summary",
              "schema": { "type": "string", "system": "summary" },
              "operations": [ "set" ]
            },
            "issuetype": {
              "required": true,
              "name": "Issue Type",
              "schema": { "type": "issuetype", "system": "issuetype" },
              "operations": []
            },
            "issuelinks": {
              "required": false,
              "name": "Linked Issues",
              "schema": { "type": "array", "items": "issuelinks", "system": "issuelinks" },
              "operations": [ "add" ]
            },
            "labels": {
              "required": false,
              "name": "Labels",
              "schema": { "type": "array", "items": "string", "system": "labels" },
              "operations": [ "add", "set", "remove" ]
            }
          }
        }
        """;

    private ProtocolSeam _seam = null!;

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _seam = await ProtocolSeam.StartAsync();

        _client = await _seam.ConnectAsync("issues:write");
    }

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task The_edit_screen_takes_a_key_and_says_it_only_reads()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var tool = tools.Single(candidate => candidate.Name is "jira_get_edit_fields");

        tool.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);

        tool.JsonSchema.GetProperty("required").EnumerateArray()
            .Select(required => required.GetString()).ShouldBe(["key"]);
    }

    [Fact]
    public async Task The_edit_screen_names_the_issue_and_every_field_on_it()
    {
        StubEditMeta("PROJ-42");

        var text = await CallAsync("PROJ-42");

        text.ShouldContain("PROJ-42 — 4 fields on the edit screen");

        // "Required" means something else here than on the create screen, and jira_update_issue
        // documents null as "clears the field" — so the label says which meaning this is.
        text.ShouldContain("required (may not be cleared)");

        text.ShouldContain("summary (Summary)");
        text.ShouldContain("labels (Labels)");
    }

    [Fact]
    public async Task A_field_that_cannot_be_written_says_so_and_a_field_that_can_stays_quiet()
    {
        StubEditMeta("PROJ-42");

        var text = await CallAsync("PROJ-42");

        // On the screen, required, and still not settable — the fact an agent can otherwise learn
        // only by being refused.
        text.ShouldContain("issuetype (Issue Type) — issuetype; not writable");

        // A link may be added and never replaced, so jira_update_issue can never write it.
        text.ShouldContain("issuelinks (Linked Issues) — array; add only");

        text.ShouldContain("labels (Labels) — array; operations: add, set, remove");

        // Almost every field accepts set and only set. Printing that on each line spends response
        // budget saying what the caller already assumes.
        text.ShouldContain("summary (Summary) — string" + Environment.NewLine);
    }

    [Fact]
    public async Task The_structured_half_carries_jiras_operations_verbatim()
    {
        StubEditMeta("PROJ-42");

        var result = await _client.CallToolAsync(
            "jira_get_edit_fields",
            new Dictionary<string, object?> { ["key"] = "PROJ-42" },
            cancellationToken: TestContext.Current.CancellationToken);

        var structure = result.StructuredContent.ShouldNotBeNull();

        structure.GetProperty("key").GetString().ShouldBe("PROJ-42");
        structure.GetProperty("totalFields").GetInt32().ShouldBe(4);

        var issueType = structure.GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("id").GetString() is "issuetype");

        issueType.GetProperty("operations").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_issue_jira_will_not_show_comes_back_as_an_error_about_the_issue()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/NOPE-1/editmeta").UsingGet())
            .RespondWith(JiraResponse.Json(404, """
                {"errorMessages":["Issue Does Not Exist"],"errors":{}}
                """));

        var result = await _client.CallToolAsync(
            "jira_get_edit_fields",
            new Dictionary<string, object?> { ["key"] = "NOPE-1" },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        // Unlike the create screen, an unknown key is a 404 rather than an empty answer, and the
        // issue-404 sentence is already the right one.
        TextOf(result).ShouldContain("issue key");
    }

    [Fact]
    public async Task A_rejected_update_is_pointed_at_the_edit_screen_rather_than_the_create_screen()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42").UsingPut())
            .RespondWith(JiraResponse.Json(400, """
                {"errorMessages":[],"errors":{"customfield_10010":"Field cannot be set."}}
                """));

        var result = await _client.CallToolAsync(
            "jira_update_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["fields"] = new Dictionary<string, object?> { ["customfield_10010"] = "x" },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var text = TextOf(result);

        // The create screen answers about a different screen, and reaching it costs a read of the
        // issue first — which is the loop this advice used to send an agent round.
        text.ShouldContain("jira_get_edit_fields");
        text.ShouldNotContain("jira_get_create_fields");
    }

    private async Task<string> CallAsync(string key)
    {
        var result = await _client.CallToolAsync(
            "jira_get_edit_fields",
            new Dictionary<string, object?> { ["key"] = key },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true, TextOf(result));

        return TextOf(result);
    }

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

    private void StubEditMeta(string key) =>
        _seam.Jira.Given(Request.Create().WithPath($"/rest/api/2/issue/{key}/editmeta").UsingGet())
            .RespondWith(JiraResponse.Json(200, EditMetaPayload));
}
