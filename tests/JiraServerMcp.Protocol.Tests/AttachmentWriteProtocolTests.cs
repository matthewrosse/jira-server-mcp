using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock.RequestBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// Uploading an attachment across the protocol seam (ADR-0008): the grant that puts the tool in
/// the list at all, one case per rule the tool refuses on — each of which must reach Jira with
/// nothing — the answer a success gives, and a spent idempotency key on a write Jira appends
/// rather than replaces.
/// </summary>
public sealed class AttachmentWriteProtocolTests : IAsyncLifetime
{
    private const string Uploaded = """
        [
          {
            "id": "10501",
            "filename": "notes.md",
            "size": 12345,
            "mimeType": "text/plain",
            "content": "https://jira.example.invalid/secure/attachment/10501/notes.md"
          }
        ]
        """;

    private const string Path = "/rest/api/2/issue/PROJ-42/attachments";

    private ProtocolSeam _seam = null!;

    public async ValueTask InitializeAsync() => _seam = await ProtocolSeam.StartAsync();

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task Without_the_attachments_grant_the_tool_is_not_in_the_list_at_all()
    {
        var client = await _seam.ConnectAsync("issues:write", "comments:write", "links:write");

        var tools = await Names(client);

        // An attachment is the one write that ships a blob a reviewer cannot skim in the issue
        // history, so every other write being granted does not grant this one.
        tools.ShouldNotContain("jira_add_attachment");

        // And the read half is unconditional, as it always was.
        tools.ShouldContain("jira_get_attachment");
    }

    [Fact]
    public async Task The_attachments_grant_registers_it_and_nothing_further()
    {
        var tools = await Names(await _seam.ConnectAsync("attachments:write"));

        tools.ShouldContain("jira_add_attachment");

        tools.ShouldNotContain("jira_create_issue");
        tools.ShouldNotContain("jira_add_comment");
    }

    [Fact]
    public async Task The_tool_takes_a_key_a_name_and_content_and_an_optional_idempotency_key()
    {
        var client = await _seam.ConnectAsync("attachments:write");

        var tool = (await client.ListToolsAsync(
                cancellationToken: TestContext.Current.CancellationToken))
            .Single(entry => entry.Name is "jira_add_attachment");

        tool.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(false);

        tool.JsonSchema.GetProperty("required").EnumerateArray()
            .Select(required => required.GetString())
            .ShouldBe(["key", "fileName", "content"]);

        tool.JsonSchema.GetProperty("properties")
            .TryGetProperty("idempotencyKey", out _).ShouldBeTrue();

        // No path parameter, now or ever: this server opens no file on the machine it runs on
        // (ADR-0012).
        tool.JsonSchema.GetProperty("properties").TryGetProperty("path", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_that_lands_is_named_sized_and_numbered_in_both_halves_of_the_answer()
    {
        StubUpload();

        var result = await AttachAsync(
            await _seam.ConnectAsync("attachments:write"),
            content: "the whole of a short test log\n");

        result.IsError.ShouldNotBe(true);

        // Jira's own size rather than this server's count of what it sent, so the answer reports
        // what was stored.
        TextOf(result).ShouldContain("Attached notes.md to PROJ-42 as attachment 10501");
        TextOf(result).ShouldContain("12345 bytes");

        // The caller wrote the content, so it is not handed back.
        TextOf(result).ShouldNotContain("the whole of a short test log");

        var structure = result.StructuredContent.ShouldNotBeNull();

        structure.GetProperty("outcome").GetString().ShouldBe("ok");
        structure.GetProperty("key").GetString().ShouldBe("PROJ-42");
        structure.GetProperty("attachmentId").GetString().ShouldBe("10501");
        structure.GetProperty("fileName").GetString().ShouldBe("notes.md");
        structure.GetProperty("size").GetInt64().ShouldBe(12_345);
    }

    [Theory]
    [InlineData("", "a line", "needs a file name")]
    [InlineData("notes\n.md", "a line", "control character")]
    [InlineData("logs/notes.md", "a line", "'/' or '\\'")]
    [InlineData("..", "a line", "names a directory")]
    [InlineData("notes.md", "", "empty file")]
    [InlineData("notes.md", "before\0after", "NUL")]
    public async Task A_refused_upload_says_which_rule_it_broke_and_sends_nothing(
        string fileName, string content, string expected)
    {
        StubUpload();

        var result = await AttachAsync(
            await _seam.ConnectAsync("attachments:write"), fileName, content);

        result.IsError.ShouldBe(true);
        TextOf(result).ShouldContain(expected);
        TextOf(result).ShouldContain("PROJ-42");

        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("outcome").GetString().ShouldBe("refused");

        // Refused here, not by Jira: the whole point of a rule this server states itself.
        Posts().ShouldBe(0);
    }

    [Fact]
    public async Task A_name_longer_than_jira_holds_is_refused_before_anything_is_sent()
    {
        StubUpload();

        var result = await AttachAsync(
            await _seam.ConnectAsync("attachments:write"),
            fileName: new string('a', 256));

        result.IsError.ShouldBe(true);
        TextOf(result).ShouldContain("255");
        Posts().ShouldBe(0);
    }

    [Fact]
    public async Task Content_past_the_cap_is_refused_with_this_servers_own_sentence()
    {
        StubUpload();

        var result = await AttachAsync(
            await _seam.ConnectAsync("attachments:write"),
            content: new string('x', 64_001));

        result.IsError.ShouldBe(true);

        // Fail-fast with a sentence naming the limit and the actual size, rather than a Jira 413
        // or a silent success at four megabytes.
        TextOf(result).ShouldContain("64000");
        TextOf(result).ShouldContain("64001");

        Posts().ShouldBe(0);
    }

    [Fact]
    public async Task A_refusal_does_not_spend_the_idempotency_key_the_corrected_call_reuses()
    {
        StubUpload();

        var client = await _seam.ConnectAsync("attachments:write");

        var refused = await AttachAsync(client, content: "", idempotencyKey: "run-42-step-3");

        refused.IsError.ShouldBe(true);

        // A call this server refused outright never reached Jira, so the key names no attempt.
        var corrected = await AttachAsync(
            client, content: "a line", idempotencyKey: "run-42-step-3");

        corrected.IsError.ShouldNotBe(true);
        TextOf(corrected).ShouldContain("Attached notes.md");

        Posts().ShouldBe(1);
    }

    [Fact]
    public async Task A_second_upload_under_a_spent_key_writes_nothing_and_names_the_first_file()
    {
        StubUpload();

        var client = await _seam.ConnectAsync("attachments:write");

        var first = await AttachAsync(client, idempotencyKey: "run-42-step-3");
        var second = await AttachAsync(client, idempotencyKey: "run-42-step-3");

        second.IsError.ShouldNotBe(true);
        TextOf(second).ShouldContain("already used");
        TextOf(second).ShouldContain("attachment 10501");

        second.StructuredContent.ShouldNotBeNull()
            .GetProperty("attachmentId").GetString().ShouldBe("10501");

        first.StructuredContent.ShouldNotBeNull()
            .GetProperty("attachmentId").GetString().ShouldBe("10501");

        // Jira's attachment endpoint appends rather than replaces, so a repeat that got through
        // would be a second file of the same name rather than a harmless overwrite.
        Posts().ShouldBe(1);
    }

    private async Task<CallToolResult> AttachAsync(
        McpClient client,
        string fileName = "notes.md",
        string content = "a line",
        string? idempotencyKey = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["key"] = "PROJ-42",
            ["fileName"] = fileName,
            ["content"] = content,
        };

        if (idempotencyKey is not null)
        {
            arguments["idempotencyKey"] = idempotencyKey;
        }

        return await client.CallToolAsync(
            "jira_add_attachment",
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<string>> Names(McpClient client) =>
    [
        .. (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name),
    ];

    private static string TextOf(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private void StubUpload() =>
        _seam.Jira.Given(Request.Create().WithPath(Path).UsingPost())
            .RespondWith(JiraResponse.Json(200, Uploaded));

    private int Posts() =>
        _seam.Jira.LogEntries
            .Select(entry => entry.RequestMessage)
            .OfType<WireMock.IRequestMessage>()
            .Count(request =>
                request.Path == Path
                && request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase));
}
