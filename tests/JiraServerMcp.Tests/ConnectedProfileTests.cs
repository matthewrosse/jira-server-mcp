using JiraServerMcp.Cli;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Tests;

/// <summary>
/// The mapping from a profile and a token to Jira client configuration — the shape three call
/// sites used to build by hand, and drifted once doing it. The failure ladder built on top of it
/// is proven at the verb seam (ADR-0008, clause 4), not here.
/// </summary>
public sealed class ConnectedProfileTests
{
    [Fact]
    public void OptionsFor_carries_the_base_url_token_and_ca_bundle_from_the_profile()
    {
        var profile = new Profile
        {
            BaseUrl = new Uri("https://jira.example.com"),
            CaBundlePath = "/etc/ssl/corporate-ca.pem",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var options = ConnectedProfile.OptionsFor(profile, "s3cr3t-token");

        options.BaseUrl.ShouldBe(profile.BaseUrl);
        options.PersonalAccessToken.ShouldBe("s3cr3t-token");
        options.CaBundlePath.ShouldBe(profile.CaBundlePath);
    }

    [Fact]
    public void OptionsFor_leaves_the_ca_bundle_path_null_when_the_profile_has_none()
    {
        var profile = new Profile
        {
            BaseUrl = new Uri("https://jira.example.com"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var options = ConnectedProfile.OptionsFor(profile, "s3cr3t-token");

        options.CaBundlePath.ShouldBeNull();
    }

    /// <summary>
    /// The variable that overrides this is read from the process environment, so the parse and
    /// refusal cases belong at the verb seam, where each test owns the environment it runs in.
    /// </summary>
    [Fact]
    public void OptionsFor_bounds_a_call_at_thirty_seconds_when_nothing_says_otherwise()
    {
        var profile = new Profile
        {
            BaseUrl = new Uri("https://jira.example.com"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        ConnectedProfile.OptionsFor(profile, "s3cr3t-token").Timeout
            .ShouldBe(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Two call sites drifted once, building `JiraClientOptions` by hand instead of asking this
    /// module. Pinned so a third caller cannot quietly reintroduce that shape.
    /// </summary>
    [Fact]
    public void No_caller_outside_this_module_mentions_JiraClientOptions()
    {
        var hostSource = Path.Combine(RepositoryRoot.Find().FullName, "src", "JiraServerMcp");
        var connectedProfileFile = Path.Combine(hostSource, "Cli", "ConnectedProfile.cs");

        var offenders = Directory.GetFiles(hostSource, "*.cs", SearchOption.AllDirectories)
            .Where(file => file != connectedProfileFile)
            .Where(file => File.ReadAllText(file).Contains("JiraClientOptions"))
            .Select(file => Path.GetRelativePath(hostSource, file));

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// A verb that builds its own container builds its own client, and then writes its own
    /// version of the failure ladder — which is how one of them came to be missing a timeout arm.
    /// The allow-list is empty on purpose: `serve` registers into the host builder's services.
    /// </summary>
    [Fact]
    public void No_verb_builds_a_service_collection_of_its_own()
    {
        var cliSource = Path.Combine(
            RepositoryRoot.Find().FullName, "src", "JiraServerMcp", "Cli");

        var connectedProfileFile = Path.Combine(cliSource, "ConnectedProfile.cs");

        var offenders = Directory.GetFiles(cliSource, "*.cs", SearchOption.AllDirectories)
            .Where(file => file != connectedProfileFile)
            .Where(file => File.ReadAllText(file).Contains("new ServiceCollection()"))
            .Select(file => Path.GetRelativePath(cliSource, file));

        offenders.ShouldBeEmpty();
    }
}
