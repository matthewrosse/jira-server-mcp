namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The staging in <see cref="ProtocolSeam"/> is only worth having if the next file uses it, so the
/// two calls it exists to replace are banned everywhere else in this project.
/// </summary>
public class ProtocolSeamGuardTests
{
    /// <summary>
    /// Spelled in halves, so the guard does not trip over its own text and need an allow-list
    /// entry for itself.
    /// </summary>
    private static readonly string[] _staging =
    [
        "WireMockServer" + ".Start(",
        "McpClient" + ".CreateAsync(",
    ];

    /// <summary>
    /// <c>CredentialRoundTripTests</c> is about logging in, so a fixture that is already logged in
    /// would invert it, and <c>StandardOutputTests</c> has no MCP client at all.
    /// </summary>
    private static readonly string[] _allowed =
    [
        "ProtocolSeam.cs",
        "CredentialRoundTripTests.cs",
        "StandardOutputTests.cs",
    ];

    [Fact]
    public void No_protocol_test_stages_the_seam_by_hand()
    {
        var directory = new DirectoryInfo(Path.Combine(
            RepositoryRoot.Find().FullName, "tests", "JiraServerMcp.Protocol.Tests"));

        foreach (var file in directory.GetFiles("*.cs"))
        {
            if (_allowed.Contains(file.Name))
            {
                continue;
            }

            var text = File.ReadAllText(file.FullName);

            foreach (var call in _staging)
            {
                text.Contains(call).ShouldBeFalse(
                    $"{file.Name} stages the protocol seam by hand. Hold a ProtocolSeam and " +
                    "call StartAsync and ConnectAsync instead.");
            }
        }
    }
}
