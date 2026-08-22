using System.Text.Json;

namespace JiraServerMcp.Tests;

/// <summary>
/// The profile verbs as an operator experiences them: driven through the program's entry point,
/// asserting exit codes, what the terminal shows, and what ends up on disk.
/// </summary>
public sealed class ProfileVerbTests : IDisposable
{
    private readonly VerbSeam _seam = new();

    public void Dispose() => _seam.Dispose();

    [Fact]
    public async Task Add_writes_the_profile_and_says_so()
    {
        var result = await AddAsync("work", "https://jira.example.com");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("work");
        File.Exists(_seam.Home.ProfilesFile).ShouldBeTrue();

        var profile = ProfileIn(_seam.Home.ReadProfiles(), "work");

        profile.GetProperty("baseUrl").GetString().ShouldBe("https://jira.example.com/");
        profile.GetProperty("createdAt").GetString().ShouldNotBeNullOrEmpty();
        profile.GetProperty("updatedAt").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Add_records_a_certificate_authority_bundle_when_one_is_given()
    {
        var bundle = await WriteBundleAsync(TestCertificate.Pem());

        var result = await _seam.RunAsync(
            ["profile", "add", "work", "--url", "https://jira.example.com", "--ca-bundle", bundle]);

        result.ExitCode.ShouldBe(0);

        ProfileIn(_seam.Home.ReadProfiles(), "work")
            .GetProperty("caBundlePath").GetString().ShouldBe(bundle);
    }

    [Theory]
    [InlineData("http://jira.example.com")]
    [InlineData("ftp://jira.example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("jira.example.com")]
    public async Task Add_rejects_a_url_that_is_neither_https_nor_explicit_localhost(string url)
    {
        var result = await AddAsync("work", url);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("HTTPS");
        File.Exists(_seam.Home.ProfilesFile).ShouldBeFalse();
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    [InlineData("https://jira.example.com/jira")]
    public async Task Add_accepts_https_and_explicit_localhost(string url)
    {
        var result = await AddAsync("work", url);

        result.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Add_refuses_a_certificate_authority_bundle_that_is_not_there()
    {
        var result = await _seam.RunAsync(
            [
                "profile", "add", "work",
                "--url", "https://jira.example.com",
                "--ca-bundle", Path.Combine(Path.GetTempPath(), "no-such-bundle.pem"),
            ]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("no-such-bundle.pem");
        File.Exists(_seam.Home.ProfilesFile).ShouldBeFalse();
    }

    [Fact]
    public async Task Add_refuses_a_certificate_authority_bundle_with_no_certificate_in_it()
    {
        // A bundle that holds nothing is worse than none at all: it becomes an empty trust store,
        // and every handshake then fails for a reason that says nothing about this file.
        var bundle = await WriteBundleAsync("not a certificate");

        var result = await _seam.RunAsync(
            [
                "profile", "add", "work",
                "--url", "https://jira.example.com",
                "--ca-bundle", bundle,
            ]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("corporate-ca.pem");
        File.Exists(_seam.Home.ProfilesFile).ShouldBeFalse();
    }

    [Fact]
    public async Task A_damaged_profile_file_is_reported_rather_than_thrown()
    {
        await _seam.AddProfileAsync("work", "https://jira.example.com");

        await File.WriteAllTextAsync(
            _seam.Home.ProfilesFile, "{ truncated", TestContext.Current.CancellationToken);

        var result = await _seam.RunAsync(["profile", "list"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("profiles.json");
        result.StandardError.ShouldNotContain("Unhandled exception");
    }

    [Fact]
    public async Task A_credential_that_cannot_be_decrypted_names_the_command_that_fixes_it()
    {
        await _seam.AddProfileAsync();
        await _seam.LoginAsync();

        // The key without its credentials, or the credentials without their key, is the same
        // situation: a restored backup that took one and not the other.
        File.Delete(Path.Combine(_seam.Home.Directory, "credentials.key"));

        var result = await _seam.RunAsync(["serve", "--profile", "work"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("auth login work");
        result.StandardError.ShouldNotContain("Unhandled exception");
    }

    [Fact]
    public async Task Storing_a_token_does_not_re_key_another_profiles_credential()
    {
        await _seam.AddProfileAsync("work");
        await _seam.AddProfileAsync("spare");
        await _seam.LoginAsync("work", "work-personal-access-token");
        await _seam.LoginAsync("spare", "spare-personal-access-token");

        // Both were encrypted under the same key, and neither login replaced it.
        var served = await _seam.RunAsync(["serve", "--profile", "work"]);

        served.StandardError.ShouldNotContain("cannot be decrypted");
        served.StandardError.ShouldNotContain("Unhandled exception");
    }

    [Fact]
    public async Task Add_refuses_to_overwrite_an_existing_profile()
    {
        await _seam.AddProfileAsync("work", "https://jira.example.com");

        var result = await AddAsync("work", "https://other.example.com");

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("work");

        ProfileIn(_seam.Home.ReadProfiles(), "work")
            .GetProperty("baseUrl").GetString().ShouldBe("https://jira.example.com/");
    }

    [Fact]
    public async Task List_shows_names_and_urls()
    {
        await _seam.AddProfileAsync("work", "https://jira.example.com");
        await _seam.AddProfileAsync("spare", "https://jira.spare.example.com");

        var result = await _seam.RunAsync(["profile", "list"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("work");
        result.StandardOutput.ShouldContain("https://jira.example.com/");
        result.StandardOutput.ShouldContain("spare");
        result.StandardOutput.ShouldContain("https://jira.spare.example.com/");
    }

    [Fact]
    public async Task List_prints_no_secret_even_when_a_credential_is_stored()
    {
        await _seam.AddProfileAsync();
        await _seam.LoginAsync();

        var result = await _seam.RunAsync(["profile", "list"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldNotContain("s3cr3t");
        result.StandardError.ShouldNotContain("s3cr3t");
    }

    [Fact]
    public async Task List_says_so_when_there_are_no_profiles()
    {
        var result = await _seam.RunAsync(["profile", "list"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("No profiles");
    }

    [Fact]
    public async Task Remove_deletes_the_profile_and_its_credential()
    {
        await _seam.AddProfileAsync();
        await _seam.LoginAsync();

        var result = await _seam.RunAsync(["profile", "remove", "work"]);

        result.ExitCode.ShouldBe(0);
        _seam.Home.ReadProfiles().ShouldNotContain("work");

        // The credential is gone with it: serving the profile again cannot find one.
        var served = await _seam.RunAsync(["serve", "--profile", "work"]);

        served.ExitCode.ShouldNotBe(0);
        served.StandardError.ShouldContain("work");
    }

    [Fact]
    public async Task Remove_reports_a_profile_that_was_never_there()
    {
        var result = await _seam.RunAsync(["profile", "remove", "absent"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("absent");
    }

    [Fact]
    public async Task The_profile_file_never_holds_a_token()
    {
        await _seam.AddProfileAsync();
        await _seam.LoginAsync();

        _seam.Home.ReadProfiles().ShouldNotContain("s3cr3t");
    }

    [Fact]
    public async Task Configuration_is_written_with_owner_only_permissions()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes do not exist on Windows.");

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await _seam.AddProfileAsync();
        await _seam.LoginAsync();

        File.GetUnixFileMode(_seam.Home.Directory).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        foreach (var file in Directory.GetFiles(_seam.Home.Directory))
        {
            File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private async Task<string> WriteBundleAsync(string contents)
    {
        var bundle = Path.Combine(_seam.Home.Directory, "corporate-ca.pem");

        Directory.CreateDirectory(_seam.Home.Directory);
        await File.WriteAllTextAsync(bundle, contents, TestContext.Current.CancellationToken);

        return bundle;
    }

    /// <summary>
    /// `profile add` where the verb is the subject rather than the staging, which is most of this
    /// file: the URL is the argument under test, so it is always given.
    /// </summary>
    private Task<HostProcessResult> AddAsync(string name, string url) =>
        _seam.RunAsync(["profile", "add", name, "--url", url]);

    private static JsonElement ProfileIn(string profilesJson, string name) =>
        JsonDocument.Parse(profilesJson).RootElement.GetProperty("profiles").GetProperty(name);
}
