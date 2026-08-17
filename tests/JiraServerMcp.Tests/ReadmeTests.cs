using System.Reflection;
using System.Text.RegularExpressions;
using JiraServerMcp.Tools;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// The README's tool catalogue is the only description of the tool surface a reader gets before
/// installing anything, so it is held to the code rather than to the design document. A tool
/// added, renamed, or moved to another grant fails here instead of quietly outliving its row.
/// </summary>
public class ReadmeTests
{
    private static readonly string _readme =
        File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, "README.md"));

    /// <summary>
    /// A catalogue row: the tool in the first cell and the grant it needs in the second, as
    /// <c>| `jira_add_comment` | `comments:write` | … |</c>.
    /// </summary>
    private static readonly Regex _row = new(
        @"^\|\s*`(?<tool>jira_[a-z_]+)`\s*\|\s*(?<grant>[^|]*?)\s*\|",
        RegexOptions.Multiline);

    /// <summary>
    /// A grant-conditional registration block in the serve verb, and the tools inside it.
    /// </summary>
    private static readonly Regex _grantBlock = new(
        @"grants\.Allows\(Grant\.(?<grant>\w+)\)\)\s*\{(?<body>[^}]*)\}",
        RegexOptions.Singleline);

    private static readonly Regex _registration = new(@"WithTools<(?<type>\w+)>");

    /// <summary>No grant needed, written as an em dash so a blank cell cannot pass for one.</summary>
    private const string None = "—";

    [Fact]
    public void The_tool_catalogue_names_every_registered_tool_and_no_others()
    {
        Catalogue().Keys.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(RegisteredTools().Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_tool_carries_the_grant_the_serve_verb_registers_it_under()
    {
        var catalogued = Catalogue();
        var granted = GrantsInTheServeVerb();

        foreach (var (tool, documented) in catalogued)
        {
            var actual = granted.GetValueOrDefault(tool, None);

            documented.Trim('`').ShouldBe(
                actual,
                $"The README documents {tool} as needing '{documented}'.");
        }
    }

    private static IReadOnlyDictionary<string, string> Catalogue() =>
        _row.Matches(_readme).ToDictionary(
            match => match.Groups["tool"].Value,
            match => match.Groups["grant"].Value);

    /// <summary>
    /// The tool each write grant registers, read from the serve verb: the registration is the
    /// only place the mapping exists, and a second copy of it here would verify nothing.
    /// </summary>
    private static IReadOnlyDictionary<string, string> GrantsInTheServeVerb()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName, "src", "JiraServerMcp", "Cli", "ServeVerb.cs"));

        var byType = RegisteredTools().ToDictionary(tool => tool.Value, tool => tool.Key);

        return _grantBlock.Matches(source)
            .SelectMany(block => _registration
                .Matches(block.Groups["body"].Value)
                .Select(registration => (
                    Tool: byType[registration.Groups["type"].Value],
                    Grant: GrantName(block.Groups["grant"].Value))))
            .ToDictionary(pair => pair.Tool, pair => pair.Grant);
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
    /// Every tool the host can register, from the attributes the MCP SDK itself reads, keyed by
    /// tool name and valued by the class that declares it.
    /// </summary>
    private static IReadOnlyDictionary<string, string> RegisteredTools() =>
        typeof(WhoamiTool).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => (
                    Name: method.GetCustomAttribute<McpServerToolAttribute>()?.Name,
                    Type: type.Name)))
            .Where(tool => tool.Name is not null)
            .ToDictionary(tool => tool.Name!, tool => tool.Type);
}
