using JiraServerMcp.Profiles;

namespace JiraServerMcp.Tests;

/// <summary>
/// The mapping from a profile and a token to Jira client configuration — the shape three call
/// sites used to build by hand, and drifted once doing it.
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
}
