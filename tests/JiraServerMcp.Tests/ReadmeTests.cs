using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using JiraServerMcp.Grants;
using JiraServerMcp.Prompts;
using JiraServerMcp.Tools;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// The README's tool catalogue is the only description of the tool surface a reader gets before
/// installing anything, so it is held to <see cref="ToolSurface"/> rather than to the design
/// document. A tool added, renamed, moved to another grant, or left unregistered fails here
/// instead of quietly outliving its row.
/// </summary>
public class ReadmeTests
{
    private static readonly string _readme =
        File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, "README.md"));

    /// <summary>
    /// A catalogue row: the tool in the first cell, the grant it needs in the second, and what it
    /// does in the third, as <c>| `jira_add_comment` | `comments:write` | … |</c>.
    /// </summary>
    private static readonly Regex _row = new(
        @"^\|\s*`(?<tool>jira_[a-z_]+)`\s*\|\s*(?<grant>[^|]*?)\s*\|(?<what>[^|]*)\|",
        RegexOptions.Multiline);

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

    /// <summary>
    /// The one limit the README states as a number. No tool shipping can falsify it — only the
    /// constant changing — so it is held here rather than beside the claimed absences.
    /// </summary>
    [Fact]
    public void The_readme_writes_the_longest_attachment_content_the_upload_takes()
    {
        var longest = AttachmentUpload.LongestContent.ToString("N0", CultureInfo.InvariantCulture);

        _readme.ShouldContain(
            longest,
            Case.Sensitive,
            $"The README states the attachment limit as a number, which is now {longest}.");
    }

    /// <summary>
    /// The workflow prompts section, held to <see cref="PromptSurface"/> the same way the tool
    /// catalogue is held to <see cref="ToolSurface"/>: a prompt added or renamed fails here rather
    /// than quietly outliving its row.
    /// </summary>
    [Fact]
    public void The_workflow_prompt_section_names_every_prompt_the_serve_verb_registers()
    {
        var documented = new Regex(@"^\|\s*`(?<prompt>[a-z_]+)`\s*\|", RegexOptions.Multiline)
            .Matches(Section("## Workflow prompts"))
            .Select(match => match.Groups["prompt"].Value)
            .ToList();

        documented.ShouldBe([ImplementIssuePrompt.Name]);
    }

    [Fact]
    public void Every_tool_a_prompt_requires_is_named_in_that_prompts_row()
    {
        // What a reader needs from this section is why a prompt they cannot see is missing, and
        // that answer is the tools it requires — so the row carries them rather than a grant.
        var section = Section("## Workflow prompts");
        var declared = Declared();

        foreach (var entry in PromptSurface.Entries)
        {
            foreach (var required in entry.RequiredTools)
            {
                var name = declared.Single(tool => tool.Value == required.Name).Key;

                section.ShouldContain(
                    name,
                    Case.Sensitive,
                    $"{entry.PromptType.Name}'s procedure calls {name}, so its row must say so.");
            }
        }
    }

    /// <summary>One section of the README, from its heading to the next one at the same level.</summary>
    private static string Section(string heading)
    {
        var start = _readme.IndexOf(heading, StringComparison.Ordinal);

        start.ShouldBeGreaterThanOrEqualTo(0, $"The README has no '{heading}' section.");

        var next = _readme.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);

        return next < 0 ? _readme[start..] : _readme[start..next];
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
    /// Every tool <see cref="ToolSurface"/> registers, and the grant it registers it under — read
    /// from the table itself, because a second copy of the mapping in this file would agree with
    /// itself and prove nothing.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Registered()
    {
        var declared = Declared();

        return ToolSurface.Entries.ToDictionary(
            entry => declared.SingleOrDefault(tool => tool.Value == entry.ToolType.Name).Key
                ?? throw new InvalidOperationException(
                    $"The tool surface registers {entry.ToolType.Name}, which declares no method "
                    + "carrying an [McpServerTool] name. Every registered class must declare "
                    + "exactly one."),
            entry => entry.RequiredGrant is { } grant ? GrantSet.Name(grant) : NoGrant);
    }

    private static IReadOnlyList<string> SoftwareTools()
    {
        var declared = Declared();

        return
        [
            .. ToolSurface.Entries
                .Where(entry => entry.RequiresSoftwareLicence)
                .Select(entry => declared.Single(tool => tool.Value == entry.ToolType.Name).Key),
        ];
    }

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
