namespace JiraServerMcp.Tests;

/// <summary>
/// The staging in <see cref="VerbSeam"/> and the payload in <see cref="JiraAccount"/> are only
/// worth having if the next file uses them, so retyping either is banned. Two guards rather than
/// one because they catch different things: a file can get its double legitimately and still
/// retype the account, which is how <c>ChangedSinceProtocolTests</c> drifted while
/// <c>ProtocolSeamGuardTests</c> watched.
/// </summary>
public class VerbSeamGuardTests
{
    /// <summary>
    /// Spelled in halves, so the guards do not trip over their own text and need an allow-list
    /// entry for themselves.
    /// </summary>
    private const string Staging = "WireMockServer" + ".Start(";

    private const string AccountStub = "WithPath(\"" + "/rest/api/2/myself\")";

    [Fact]
    public void No_verb_test_stages_the_seam_by_hand()
    {
        foreach (var (file, text) in FilesIn("JiraServerMcp.Tests"))
        {
            // The fixture is where the call moved to, not a file exempted from the rule. No test
            // is on an allow-list here: a verb test with no double at all and one whose Jira is a
            // TLS server elsewhere are honest states of the seam, which is why it starts its
            // double lazily rather than carving them out.
            if (file.Name is "VerbSeam.cs")
            {
                continue;
            }

            text.Contains(Staging).ShouldBeFalse(
                $"{file.Name} starts a Jira double by hand. Hold a VerbSeam and reach its Jira, "
                + "or call AddProfileAsync and LoginAsync, instead.");
        }
    }

    /// <summary>
    /// A file stays free to answer the account call however it likes — a 401, a delay, a body that
    /// is not JSON, a match on the bearer token are all things a test is entitled to stage. What it
    /// may not do is retype the account, because the fields nothing asserts on are exactly the ones
    /// that drift. Both seams are scanned; the wire seam is not, because there the payload's
    /// content is deliberately irrelevant and a response only that test knows is the point.
    /// </summary>
    [Fact]
    public void No_test_retypes_the_account_payload()
    {
        var projects = new[] { "JiraServerMcp.Tests", "JiraServerMcp.Protocol.Tests" };

        foreach (var (file, text) in projects.SelectMany(FilesIn))
        {
            if (!text.Contains(AccountStub))
            {
                continue;
            }

            text.Contains(nameof(JiraAccount)).ShouldBeTrue(
                $"{file.Name} answers /rest/api/2/myself without reading the account from "
                + "JiraAccount.Payload(). Jira's user resource is one fact, and a thinner payload "
                + "teaches the next author a thinner Jira than they will meet.");
        }
    }

    private static IEnumerable<(FileInfo File, string Text)> FilesIn(string project)
    {
        var directory = new DirectoryInfo(
            Path.Combine(RepositoryRoot.Find().FullName, "tests", project));

        return directory.GetFiles("*.cs")
            .Select(file => (file, File.ReadAllText(file.FullName)));
    }
}
