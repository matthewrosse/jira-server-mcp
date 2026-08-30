using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// Uploading an attachment against an HTTP double: the multipart body Jira's attachment servlet
/// wants, the header it refuses an upload without, and the array it answers with read down to the
/// one file that was sent.
/// </summary>
public sealed class JiraAttachmentWriteTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string Created = """
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
    public async Task The_file_is_posted_as_one_multipart_part_named_file()
    {
        StubUpload();

        await AddAsync("PROJ-42", "notes.md", "the first line\n");

        var request = Upload();

        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/rest/api/2/issue/PROJ-42/attachments");

        var body = Body(request);

        // Jira's servlet reads the part named "file"; anything else is ignored and the upload
        // succeeds having stored nothing.
        body.ShouldContain("name=file");
        body.ShouldContain("filename=notes.md");
        body.ShouldContain("the first line");
    }

    [Fact]
    public async Task A_file_name_that_needs_quoting_in_the_header_gets_it()
    {
        StubUpload();

        // The name is a human's label, so spaces are accepted (ADR-0012) — and an unquoted space
        // in a Content-Disposition header would end the value early, which is the failure the
        // validation rules cannot prevent and the header writer must.
        await AddAsync("PROJ-42", "release notes.md", "a line");

        Body(Upload()).ShouldContain("filename=\"release notes.md\"");
    }

    [Fact]
    public async Task The_part_is_declared_as_utf_8_text_whatever_the_file_is_called()
    {
        StubUpload();

        // An agent-authored .html stored under its own type and served by Jira is a stored
        // cross-site-scripting shape; as text/plain it is inert (ADR-0012).
        await AddAsync("PROJ-42", "report.html", "<script>alert(1)</script>");

        Body(Upload()).ShouldContain("Content-Type: text/plain; charset=utf-8");
    }

    [Fact]
    public async Task The_upload_carries_the_header_jira_refuses_an_attachment_without()
    {
        StubUpload();

        await AddAsync("PROJ-42", "notes.md", "a line");

        Upload().Headers.ShouldNotBeNull()["X-Atlassian-Token"].ShouldHaveSingleItem()
            .ShouldBe("no-check");
    }

    [Fact]
    public async Task No_other_request_this_client_makes_carries_it()
    {
        // The header belongs to the attachment servlet rather than to a personal-access-token
        // client, so it is set on the one request and not on the HttpClient (ADR-0012).
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "name": "ada", "displayName": "Ada" }"""));

        await CreateClient().GetMyselfAsync(TestContext.Current.CancellationToken);

        var request = _jira.LogEntries.Single().RequestMessage.ShouldNotBeNull();

        var headers = request.Headers.ShouldNotBeNull();

        headers.ContainsKey("X-Atlassian-Token").ShouldBeFalse();
    }

    [Fact]
    public async Task Jiras_array_of_one_is_read_as_the_attachment_that_was_created()
    {
        StubUpload();

        var added = await AddAsync("PROJ-42", "notes.md", "a line");

        added.Id.ShouldBe("10501");
        added.FileName.ShouldBe("notes.md");

        // Jira's own count of what it stored, rather than this server's count of what it sent.
        added.Size.ShouldBe(12_345);
    }

    [Fact]
    public async Task An_answer_that_is_not_one_attachment_is_a_fault_rather_than_a_guess()
    {
        // One file was sent, so one element is the only answer this client can report against the
        // file the caller named.
        StubUpload("[]");

        var upload = async () => await AddAsync("PROJ-42", "notes.md", "a line");

        (await upload.ShouldThrowAsync<InvalidOperationException>())
            .Message.ShouldContain("notes.md");
    }

    [Fact]
    public async Task An_attachment_jira_numbered_the_identifier_of_is_still_named()
    {
        // The same disagreement the read side has: Jira's two shapes for an id do not agree on
        // whether it is quoted, and an upload nobody can name is an upload nobody can read back.
        StubUpload("""[ { "id": 10501, "filename": "notes.md", "size": 6 } ]""");

        var added = await AddAsync("PROJ-42", "notes.md", "a line");

        added.Id.ShouldBe("10501");
    }

    [Fact]
    public async Task An_attachment_jira_gave_no_identifier_to_cannot_be_reported()
    {
        StubUpload("""[ { "filename": "notes.md", "size": 6 } ]""");

        var upload = async () => await AddAsync("PROJ-42", "notes.md", "a line");

        (await upload.ShouldThrowAsync<InvalidOperationException>())
            .Message.ShouldContain("notes.md");
    }

    private async Task<Models.JiraAttachment> AddAsync(
        string key, string fileName, string content) =>
        await CreateClient().AddAttachmentAsync(
            key, fileName, content, TestContext.Current.CancellationToken);

    private void StubUpload(string body = Created) =>
        _jira.Given(Request.Create()
                .WithPath("/rest/api/2/issue/PROJ-42/attachments")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private WireMock.IRequestMessage Upload() =>
        _jira.LogEntries
            .Select(entry => entry.RequestMessage)
            .OfType<WireMock.IRequestMessage>()
            .Single(request => request.Path is "/rest/api/2/issue/PROJ-42/attachments");

    private static string Body(WireMock.IRequestMessage request) =>
        request.Body ?? throw new InvalidOperationException("The upload carried no body.");

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
