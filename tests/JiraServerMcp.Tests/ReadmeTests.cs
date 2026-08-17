using System.Reflection;
using System.Text.RegularExpressions;
using JiraServerMcp.Tools;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// The README's tool catalogue is the only description of the tool surface a reader gets before
/// installing anything, so it is held to the serve verb rather than to the design document. A tool
/// added, renamed, moved to another grant, or left unregistered fails here instead of quietly
/// outliving its row.
/// </summary>
public class ReadmeTests
{
    private static readonly string _readme =
        File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, "README.md"));

    private static readonly string _serveVerb = File.ReadAllText(Path.Combine(
        RepositoryRoot.Find().FullName, "src", "JiraServerMcp", "Cli", "ServeVerb.cs"));

    /// <summary>
    /// A catalogue row: the tool in the first cell, the grant it needs in the second, and what it
    /// does in the third, as <c>| `jira_add_comment` | `comments:write` | … |</c>.
    /// </summary>
    private static readonly Regex _row = new(
        @"^\|\s*`(?<tool>jira_[a-z_]+)`\s*\|\s*(?<grant>[^|]*?)\s*\|(?<what>[^|]*)\|",
        RegexOptions.Multiline);

    /// <summary>A grant-conditional registration block, and the tools inside it.</summary>
    private static readonly Regex _grantBlock = new(
        @"grants\.Allows\(Grant\.(?<grant>\w+)\)\)\s*\{(?<body>[^}]*)\}");

    /// <summary>The block that registers the Jira Software tools where the probe found a licence.</summary>
    private static readonly Regex _softwareBlock = new(
        @"SoftwareLicensed: true \}\)\s*\{(?<body>[^}]*)\}");

    private static readonly Regex _registration = new(@"WithTools<(?<type>\w+)>");

    /// <summary>No grant needed, written as an em dash so a blank cell cannot pass for one.</summary>
    private const string NoGrant = "—";

    [Fact]
    public void The_tool_catalogue_names_every_tool_the_serve_verb_registers_and_no_others()
    {
        Catalogue().Keys.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(Registered().Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_tool_the_assembly_declares_is_registered_somewhere()
    {
        // A tool class nobody wired up is not a documentation problem, but it is invisible in
        // exactly the same way, and this is the test that can see it.
        Registered().Keys.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(Declared().Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_tool_carries_the_grant_the_serve_verb_registers_it_under()
    {
        foreach (var (tool, documented) in Catalogue())
        {
            documented.Grant.Trim('`').ShouldBe(
                Registered()[tool],
                $"The README documents {tool} as needing '{documented.Grant}'.");
        }
    }

    [Fact]
    public void A_tool_that_needs_jira_software_says_so()
    {
        // Registered unconditionally on a Jira Core instance these would always 404, so which
        // ones need the licence is not a detail a reader can be left to discover.
        var catalogue = Catalogue();

        foreach (var tool in SoftwareTools())
        {
            catalogue[tool].What.ShouldContain(
                "Jira Software only",
                Case.Sensitive,
                $"{tool} is registered only where Jira Software is licensed.");
        }

        catalogue
            .Where(row => row.Value.What.Contains("Jira Software only", StringComparison.Ordinal))
            .Select(row => row.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(SoftwareTools().OrderBy(name => name, StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, (string Grant, string What)> Catalogue()
    {
        var rows = _row.Matches(_readme)
            .Select(match => (
                Tool: match.Groups["tool"].Value,
                Grant: match.Groups["grant"].Value,
                What: match.Groups["what"].Value))
            .ToList();

        var twice = rows.GroupBy(row => row.Tool).Where(group => group.Count() > 1).ToList();

        twice.ShouldBeEmpty(
            $"The README's catalogue names {string.Join(", ", twice.Select(group => group.Key))} "
            + "more than once.");

        return rows.ToDictionary(row => row.Tool, row => (row.Grant, row.What));
    }

    /// <summary>
    /// Every tool the serve verb registers, and the grant it registers it under — read from the
    /// registration itself, because a second copy of the mapping in this file would agree with
    /// itself and prove nothing.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Registered()
    {
        var granted = _grantBlock.Matches(_serveVerb).SelectMany(block => ToolsIn(block)
            .Select(tool => (Tool: tool, Grant: GrantName(block.Groups["grant"].Value))));

        var registered = ToolsIn(_serveVerb).ToDictionary(tool => tool, _ => NoGrant);

        foreach (var (tool, grant) in granted)
        {
            registered[tool] = grant;
        }

        return registered;
    }

    private static IReadOnlyList<string> SoftwareTools() =>
        [.. _softwareBlock.Matches(_serveVerb).SelectMany(ToolsIn)];

    /// <summary>
    /// The tool names registered in one stretch of the serve verb. A registered class that carries
    /// no tool method is a failure with a sentence rather than a <c>KeyNotFoundException</c>.
    /// </summary>
    private static IReadOnlyList<string> ToolsIn(Match block) => ToolsIn(block.Groups["body"].Value);

    private static IReadOnlyList<string> ToolsIn(string source)
    {
        var declared = Declared();

        return
        [
            .. _registration.Matches(source)
                .Select(registration => registration.Groups["type"].Value)
                .Select(type => declared.SingleOrDefault(tool => tool.Value == type).Key
                    ?? throw new InvalidOperationException(
                        $"The serve verb registers {type}, which declares no method carrying an "
                        + "[McpServerTool] name. Every registered class must declare exactly one.")),
        ];
    }

    /// <summary>The name the operator writes, from the name the enum member carries.</summary>
    private static string GrantName(string member) => member switch
    {
        "IssuesWrite" => "issues:write",
        "CommentsWrite" => "comments:write",
        "WorklogsWrite" => "worklogs:write",
        _ => throw new InvalidOperationException(
            $"The serve verb registers tools under Grant.{member}, which this test does not know. "
            + "A new grant needs a row in the README's catalogue and a name here."),
    };

    /// <summary>
    /// Every tool the assembly declares, from the attributes the MCP SDK itself reads: keyed by
    /// tool name, valued by the class that declares it. Static methods count — the SDK registers
    /// those too, and a tool missing from here would fail the catalogue test for being documented.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Declared()
    {
        var declared = typeof(WhoamiTool).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(method => (
                    Name: method.GetCustomAttribute<McpServerToolAttribute>()?.Name,
                    Type: type.Name)))
            .Where(tool => tool.Name is not null)
            .ToList();

        var twice = declared.GroupBy(tool => tool.Type).Where(group => group.Count() > 1).ToList();

        // One tool per class is what lets a registration name a tool. Splitting a class is fine;
        // this test then needs to map registrations to several names, and says so rather than
        // throwing from inside a dictionary.
        twice.ShouldBeEmpty(
            $"{string.Join(", ", twice.Select(group => group.Key))} declares more than one tool, "
            + "which this test cannot map back from a WithTools<> registration.");

        return declared.ToDictionary(tool => tool.Name!, tool => tool.Type);
    }
}
