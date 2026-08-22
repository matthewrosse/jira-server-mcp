using System.Text;
using JiraServerMcp.Jira.Models;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// Reading an attachment against an HTTP double: what is asked for, and what is made of a Jira
/// that answers the range and one that ignores it. The wire seam is also where the content URL's
/// host is proven not to be followed — Jira composes that URL from its own configured base, which
/// on an instance behind a proxy is regularly somewhere this server was never pointed at.
/// </summary>
public sealed class JiraAttachmentTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

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
    public async Task The_metadata_carries_the_name_the_size_and_where_the_bytes_are()
    {
        StubMetadata();

        var attachment = await CreateClient()
            .GetAttachmentAsync("10100", TestContext.Current.CancellationToken);

        attachment.Id.ShouldBe("10100");
        attachment.FileName.ShouldBe("server.log");
        attachment.Size.ShouldBe(2_048);
        attachment.MimeType.ShouldBe("application/octet-stream");
        attachment.Content.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_window_is_asked_for_as_a_range_so_that_resuming_costs_only_what_is_left()
    {
        StubContent("0123456789", partial: true);

        var window = await ReadAsync(Attachment(size: 10), offset: 4, window: 3);

        Encoding.UTF8.GetString(window).ShouldBe("012");

        var request = ContentRequest();

        request.Headers.ShouldNotBeNull()["Range"].ShouldHaveSingleItem().ShouldBe("bytes=4-6");
    }

    [Fact]
    public async Task The_range_stops_at_the_end_of_the_file_rather_than_at_the_end_of_the_window()
    {
        StubContent("0123456789", partial: true);

        await ReadAsync(Attachment(size: 10), offset: 0, window: 16_000);

        // Jira's attachment servlet answers a range that overruns the file by declaring the
        // requested length and sending only what it has, which arrives as a response that ended
        // prematurely. Asking only for bytes that exist is what stops it happening.
        ContentRequest().Headers.ShouldNotBeNull()["Range"].ShouldHaveSingleItem()
            .ShouldBe("bytes=0-9");
    }

    [Fact]
    public async Task A_window_of_a_file_whose_size_jira_withheld_asks_for_the_whole_window()
    {
        StubContent("0123456789", partial: true);

        var unsized = new JiraAttachment(
            "10100",
            "server.log",
            0,
            null,
            $"{_jira.Url}/secure/attachment/10100/server.log");

        await ReadAsync(unsized, offset: 0, window: 16);

        ContentRequest().Headers.ShouldNotBeNull()["Range"].ShouldHaveSingleItem()
            .ShouldBe("bytes=0-15");
    }

    [Fact]
    public async Task An_offset_at_the_end_of_a_file_is_no_bytes_without_troubling_jira()
    {
        StubContent("0123456789", partial: false);

        var window = await ReadAsync(Attachment(size: 10), offset: 10, window: 16);

        window.ShouldBeEmpty();

        // Unsatisfiable by definition, so the round trip to be told so is not worth making.
        _jira.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_jira_that_ignores_the_range_is_read_past_rather_than_believed()
    {
        // Some proxies strip the header and answer 200 with the whole file. The bytes a caller
        // asked for are then the ones after the offset, not the ones at the front.
        StubContent("0123456789", partial: false);

        var window = await ReadAsync(Attachment(size: 10), offset: 4, window: 3);

        Encoding.UTF8.GetString(window).ShouldBe("456");
    }

    [Fact]
    public async Task A_window_reaching_past_the_end_of_the_file_carries_what_was_there()
    {
        StubContent("0123456789", partial: false);

        var window = await ReadAsync(Attachment(size: 10), offset: 8, window: 100);

        Encoding.UTF8.GetString(window).ShouldBe("89");
    }

    [Fact]
    public async Task The_content_url_is_taken_as_a_path_so_a_jira_cannot_send_the_token_elsewhere()
    {
        StubContent("0123456789", partial: false);

        // Jira's own base URL says somewhere this server was never pointed at, and this profile's
        // credential and certificate authority bundle belong to the host it was.
        var elsewhere = new JiraAttachment(
            "10100",
            "server.log",
            10,
            null,
            "https://attacker.invalid/secure/attachment/10100/server.log");

        await ReadAsync(elsewhere, offset: 0, window: 10);

        ContentRequest().Path.ShouldBe("/secure/attachment/10100/server.log");
    }

    [Fact]
    public async Task A_range_past_the_end_of_the_file_is_no_bytes_rather_than_a_failure()
    {
        // "What is at byte 14 of a 14-byte file" is an honest question with an honest answer, and
        // a server gives it as a 416 rather than as an empty body. Raising that as a Jira failure
        // would tell the caller its request was refused when the file simply ended.
        _jira.Given(Request.Create().WithPath("/secure/attachment/10100/server.log").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(416));

        var window = await ReadAsync(Attachment(size: 14), offset: 14, window: 100);

        window.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_content_path_is_resolved_against_the_configured_base_rather_than_refused()
    {
        StubContent("0123456789", partial: false);

        // Some instances and proxies answer with a path rather than an absolute URL. Only the path
        // is ever used, so there is nothing to refuse.
        var relative = new JiraAttachment(
            "10100",
            "server.log",
            10,
            null,
            "/secure/attachment/10100/server.log");

        var window = await ReadAsync(relative, offset: 0, window: 10);

        Encoding.UTF8.GetString(window).ShouldBe("0123456789");
    }

    [Fact]
    public async Task An_attachment_jira_will_not_say_where_to_find_cannot_be_read()
    {
        var nowhere = new JiraAttachment("10100", "server.log", 10, null, Content: null);

        var reading = async () => await ReadAsync(nowhere, offset: 0, window: 10);

        (await reading.ShouldThrowAsync<InvalidOperationException>())
            .Message.ShouldContain("10100");
    }

    [Fact]
    public async Task An_attachment_whose_identifier_jira_numbered_is_still_listed()
    {
        // Jira's two shapes for the same value disagree: the attachment endpoint numbers the id,
        // and an issue's attachment field is documented to quote it. An instance that numbers it
        // there too would drop the file out of the listing entirely, and the agent would conclude
        // there was nothing to read.
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "key": "PROJ-12",
                      "fields": {
                        "attachment": [
                          { "id": 10100, "filename": "server.log", "size": 2048 }
                        ]
                      }
                    }
                    """));

        var issue = await CreateClient().GetIssueAsync(
            "PROJ-12",
            new IssueRead(["attachment"], [], ["attachment"], RemoteLinks: false),
            TestContext.Current.CancellationToken);

        var attachment = issue.Attachments.ShouldHaveSingleItem();

        attachment.Id.ShouldBe("10100");
        attachment.FileName.ShouldBe("server.log");
    }

    private async Task<byte[]> ReadAsync(JiraAttachment attachment, long offset, int window) =>
        await CreateClient().ReadAttachmentAsync(
            attachment,
            offset,
            window,
            TestContext.Current.CancellationToken);

    private JiraAttachment Attachment(long size) =>
        new("10100", "server.log", size, "text/plain", $"{_jira.Url}/secure/attachment/10100/server.log");

    private void StubMetadata() =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/attachment/10100").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($$"""
                    {
                      "id": 10100,
                      "filename": "server.log",
                      "size": 2048,
                      "mimeType": "application/octet-stream",
                      "content": "{{_jira.Url}}/secure/attachment/10100/server.log"
                    }
                    """));

    /// <summary>
    /// A Jira that honours the range answers 206 with the window; one that does not answers 200
    /// with the file. The double sends the same body either way, so what differs is only the
    /// status — which is exactly what the client branches on.
    /// </summary>
    private void StubContent(string body, bool partial) =>
        _jira.Given(Request.Create().WithPath("/secure/attachment/10100/server.log").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(partial ? 206 : 200)
                .WithHeader("Content-Type", "application/octet-stream")
                .WithBody(body));

    private WireMock.IRequestMessage ContentRequest() =>
        _jira.LogEntries
            .Select(entry => entry.RequestMessage)
            .OfType<WireMock.IRequestMessage>()
            .Single(request => request.Path is "/secure/attachment/10100/server.log");

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
