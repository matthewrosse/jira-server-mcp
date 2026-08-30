namespace JiraServerMcp.Tests;

/// <summary>
/// ADR-0006: JiraClient had no internal seam, so every new tool added a method to the same file.
/// The 2026-08-18 split gave it one — a partial file per resource — and these pin both halves of
/// what that was for: the client as a whole, and any one file a reader has to load to change one
/// part of it.
/// </summary>
public class JiraClientGrowthTests
{
    /// <summary>
    /// The glob rather than one file, so that the guard survived its own remedy: a split leaves
    /// the total where it was, plus the using blocks each new file repeats.
    /// </summary>
    [Fact]
    public void JiraClient_files_stay_under_the_adr_0006_line_budget()
    {
        // Set from what the client measures with the attachment upload in — 1,396 across thirteen
        // files — with about one feature batch of headroom, which is the headroom every earlier
        // number left. A tighter number would fire as noise on ordinary work; a looser one would
        // fire only after the client was already unpleasant to read.
        Total().ShouldBeLessThan(1_550,
            "JiraClient*.cs has grown past the ADR-0006 budget. The client is already split by " +
            "resource, so the answer is a new partial file for the resource being added, or a " +
            "deliberate amendment to ADR-0006 recording why the total should be larger.");
    }

    /// <summary>
    /// What the split was actually for. The cost ADR-0006 guards against is the context a reader
    /// must load to change one small part of the client, and after a split that cost is a
    /// property of the largest file rather than of the sum.
    /// </summary>
    [Fact]
    public void No_one_JiraClient_file_grows_back_into_the_file_the_split_broke_up()
    {
        foreach (var file in Files())
        {
            File.ReadAllLines(file.FullName).Length.ShouldBeLessThan(250,
                $"{file.Name} has grown past the ADR-0006 per-file budget. Endpoints for a " +
                "resource of its own belong in a partial file of their own.");
        }
    }

    private static int Total() => Files().Sum(file => File.ReadAllLines(file.FullName).Length);

    private static FileInfo[] Files() =>
        new DirectoryInfo(Path.Combine(RepositoryRoot.Find().FullName, "src", "JiraServerMcp.Jira"))
            .GetFiles("JiraClient*.cs");
}
