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
    public async Task An_attachment_jira_will_not_say_where_to_find_cannot_be_read()
    {
        var nowhere = new JiraAttachment("10100", "server.log", 10, null, Content: null);

        var reading = async () => await ReadAsync(nowhere, offset: 0, window: 10);

        (await reading.ShouldThrowAsync<InvalidOperationException>())
            .Message.ShouldContain("10100");
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
