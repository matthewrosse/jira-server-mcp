using System.Text.Json;

namespace JiraServerMcp.Tests;

/// <summary>
/// The profile verbs as an operator experiences them: driven through the program's entry point,
/// asserting exit codes, what the terminal shows, and what ends up on disk.
/// </summary>
public sealed class ProfileVerbTests : IDisposable
{
    private readonly ConfigurationHome _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public async Task Add_writes_the_profile_and_says_so()
    {
        var result = await AddAsync("work", "https://jira.example.com");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("work");
        File.Exists(_home.ProfilesFile).ShouldBeTrue();

        var profile = ProfileIn(_home.ReadProfiles(), "work");

        profile.GetProperty("baseUrl").GetString().ShouldBe("https://jira.example.com/");
        profile.GetProperty("createdAt").GetString().ShouldNotBeNullOrEmpty();
        profile.GetProperty("updatedAt").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Add_records_a_certificate_authority_bundle_when_one_is_given()
    {
        var bundle = Path.Combine(Path.GetTempPath(), "corporate-ca.pem");

        var result = await RunAsync(
            ["profile", "add", "work", "--url", "https://jira.example.com", "--ca-bundle", bundle]);

        result.ExitCode.ShouldBe(0);

        ProfileIn(_home.ReadProfiles(), "work")
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
        File.Exists(_home.ProfilesFile).ShouldBeFalse();
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("https://jira.example.com/jira")]
    public async Task Add_accepts_https_and_explicit_localhost(string url)
    {
        var result = await AddAsync("work", url);

        result.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Add_refuses_to_overwrite_an_existing_profile()
    {
        await AddAsync("work", "https://jira.example.com");

        var result = await AddAsync("work", "https://other.example.com");

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("work");

        ProfileIn(_home.ReadProfiles(), "work")
            .GetProperty("baseUrl").GetString().ShouldBe("https://jira.example.com/");
    }

    [Fact]
    public async Task List_shows_names_and_urls()
    {
        await AddAsync("work", "https://jira.example.com");
        await AddAsync("spare", "https://jira.spare.example.com");

        var result = await RunAsync(["profile", "list"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("work");
        result.StandardOutput.ShouldContain("https://jira.example.com/");
        result.StandardOutput.ShouldContain("spare");
        result.StandardOutput.ShouldContain("https://jira.spare.example.com/");
    }

    [Fact]
    public async Task List_prints_no_secret_even_when_a_credential_is_stored()
    {
        await AddAsync("work", "https://jira.example.com");
        await LoginAsync("work", "s3cr3t-personal-access-token");

        var result = await RunAsync(["profile", "list"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldNotContain("s3cr3t");
        result.StandardError.ShouldNotContain("s3cr3t");
    }

    [Fact]
    public async Task List_says_so_when_there_are_no_profiles()
    {
        var result = await RunAsync(["profile", "list"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("No profiles");
    }

    [Fact]
    public async Task Remove_deletes_the_profile_and_its_credential()
    {
        await AddAsync("work", "https://jira.example.com");
        await LoginAsync("work", "s3cr3t-personal-access-token");

        var result = await RunAsync(["profile", "remove", "work"]);

        result.ExitCode.ShouldBe(0);
        _home.ReadProfiles().ShouldNotContain("work");

        // The credential is gone with it: serving the profile again cannot find one.
        var served = await RunAsync(["serve", "--profile", "work"]);

        served.ExitCode.ShouldNotBe(0);
        served.StandardError.ShouldContain("work");
    }

    [Fact]
    public async Task Remove_reports_a_profile_that_was_never_there()
    {
        var result = await RunAsync(["profile", "remove", "absent"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("absent");
    }

    [Fact]
    public async Task The_profile_file_never_holds_a_token()
    {
        await AddAsync("work", "https://jira.example.com");
        await LoginAsync("work", "s3cr3t-personal-access-token");

        _home.ReadProfiles().ShouldNotContain("s3cr3t");
    }

    [Fact]
    public async Task Configuration_is_written_with_owner_only_permissions()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes do not exist on Windows.");

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await AddAsync("work", "https://jira.example.com");
        await LoginAsync("work", "s3cr3t-personal-access-token");

        File.GetUnixFileMode(_home.Directory).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        foreach (var file in Directory.GetFiles(_home.Directory))
        {
            File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private Task<HostProcessResult> AddAsync(string name, string url) =>
        RunAsync(["profile", "add", name, "--url", url]);

    private Task<HostProcessResult> LoginAsync(string name, string token) =>
        RunAsync(["auth", "login", name], standardInput: token + "\n");

    private Task<HostProcessResult> RunAsync(string[] verb, string? standardInput = null) =>
        HostProcess.RunAsync(
            verb,
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput);

    private static JsonElement ProfileIn(string profilesJson, string name) =>
        JsonDocument.Parse(profilesJson).RootElement.GetProperty("profiles").GetProperty(name);
}
