using JiraServerMcp.Grants;
using JiraServerMcp.Profiles;
using JiraServerMcp.Tools;
using WireMock.RequestBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The complete tool surface across the protocol seam (ADR-0008): what <c>tools/list</c> answers is
/// what <see cref="ToolSurface"/> says this run registers, built-in tools and operator-defined
/// queries together. The only place registration can be proven, because the MCP SDK's batch
/// registration once compiled, passed every container-shaped test, and still answered "Method … is
/// not available" to every call.
/// </summary>
public sealed class ToolSurfaceProtocolTests : IAsyncLifetime
{
    private ProtocolSeam _seam = null!;

    public async ValueTask InitializeAsync() => _seam = await ProtocolSeam.StartAsync();

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task Tools_list_is_exactly_the_tool_surface_this_run_registers()
    {
        // A licensed probe, so the Jira Software rows are on the surface too.
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(JiraResponse.Json(200,
                """{ "version": "8.20.7", "deploymentType": "Server" }"""));

        _seam.Jira.Given(Request.Create().WithPath("/rest/agile/1.0/board").UsingGet())
            .RespondWith(JiraResponse.Json(200,
                """{ "startAt": 0, "maxResults": 1, "isLast": true, "values": [] }"""));

        await _seam.RunAsync(["profile", "refresh", ProtocolSeam.Profile]);

        // Declaring a query runs it once, so Jira has to accept the search.
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(JiraResponse.Json(200,
                """{ "startAt": 0, "maxResults": 1, "total": 0, "issues": [] }"""));

        foreach (var name in new[] { "sprint_bugs", "blocked" })
        {
            await _seam.RunAsync(
            [
                "profile", "query", "add", ProtocolSeam.Profile, name,
                "--jql", $"labels = {name}", "--description", $"The {name} query.",
            ]);
        }

        // Some grants and not others, so the gate has something to leave out.
        string[] allowed = ["issues:write", "comments:write"];

        var client = await _seam.ConnectAsync(allowed);

        // The profile the server itself read, rather than a second copy of what was staged.
        var profile = new ProfileStore(_seam.Home.Directory).Find(ProtocolSeam.Profile)
            .ShouldNotBeNull();

        var expected = ToolSurface.For(GrantSet.Parse(allowed), profile).Names;

        // Both halves are on the expected side, or a comparison with one missing would pass.
        expected.ShouldContain("jira_list_boards");
        expected.ShouldContain("jira_q_sprint_bugs");
        expected.ShouldContain("jira_q_blocked");

        var listed = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // As sets: the order a client is shown tools in is nothing an agent is owed.
        listed.Select(tool => tool.Name).Order(StringComparer.Ordinal)
            .ShouldBe(expected.Order(StringComparer.Ordinal));
    }
}
