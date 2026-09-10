using System.Text.RegularExpressions;

namespace JiraServerMcp.Tests;

/// <summary>
/// A contract lives beside its producer (ADR-0009, as amended). The record a tool declares as its
/// output schema is referenced in two places — the module that builds it and the tool's
/// <c>OutputSchemaType</c> — and a record kept anywhere else is a third file to open for every
/// change to one rendered answer, which is how a catalogue of them grew to 878 lines.
/// </summary>
/// <remarks>
/// Only the envelopes are held to it. A row record rides inside one envelope and is built beside
/// it, and the one row with two producers, <c>IssueRowOutput</c>, is the reason the shared file
/// still exists. The schema is generated from a record's shape rather than its name or its file,
/// so the move this asks for never changes a byte a client receives.
/// </remarks>
public class OutputContractGuardTests
{
    private static readonly Regex _envelope = new(@"\brecord\s+(\w+)\s*:\s*ToolOutput\b");

    [Fact]
    public void Every_output_contract_is_declared_in_the_file_that_builds_it()
    {
        var declared = SourceFiles()
            .SelectMany(source => _envelope.Matches(source.Text)
                .Select(match => (source.File, source.Text, Name: match.Groups[1].Value)))
            .ToArray();

        // A guard that matches nothing passes forever, so the sweep has to have found something.
        declared.ShouldNotBeEmpty();

        foreach (var (file, text, name) in declared)
        {
            Regex.IsMatch(text, $@"\bnew\s+{name}\b").ShouldBeTrue(
                $"{file.Name} declares {name} but never builds one. An output contract lives in "
                + "the rendering module that produces it: move the record into that file. The "
                + "schema is generated from the record's shape, so the move changes nothing a "
                + "client receives.");
        }
    }

    private static IEnumerable<(FileInfo File, string Text)> SourceFiles()
    {
        var source = new DirectoryInfo(Path.Combine(RepositoryRoot.Find().FullName, "src"));

        return source.GetFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "obj" or "bin"))
            .Select(file => (file, File.ReadAllText(file.FullName)));
    }
}
