using JiraServerMcp.Configuration;
using JiraServerMcp.Credentials;

namespace JiraServerMcp.Tests;

/// <summary>
/// Which store a run ends up with. The Linux case is the one that matters: a box reached over
/// SSH, or WSL, where secret-tool is installed and cannot reach a keyring at all.
/// </summary>
public sealed class CredentialStoreSelectorTests : IDisposable
{
    private readonly ConfigurationHome _home = new();
    private readonly FakeSecretTool _secretTool = new();
    private readonly StringWriter _log = new();

    private CredentialStoreSelector Selector => new(
        new SecretServiceCredentialStore(_secretTool),
        new FileCredentialStore(_home.Directory));

    public void Dispose()
    {
        _home.Dispose();
        _log.Dispose();
    }

    [Fact]
    public async Task A_reachable_native_store_is_the_one_used()
    {
        var store = await SelectAsync(CredentialStoreChoice.Auto);

        store.Describe().ShouldContain("Secret Service");
        _log.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_session_with_no_reachable_keyring_falls_back_to_the_file_store_and_says_so_once()
    {
        _secretTool.HasSecretService = false;

        var store = await SelectAsync(CredentialStoreChoice.Auto);

        store.Describe().ShouldContain("encrypted file");

        var lines = _log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.ShouldHaveSingleItem().ShouldContain("encrypted file");

        // And the fallback is a working store, not just a message about one.
        await store.SetAsync("work", "s3cr3t-personal-access-token", TestContext.Current.CancellationToken);

        (await store.GetAsync("work", TestContext.Current.CancellationToken))
            .ShouldBe("s3cr3t-personal-access-token");
    }

    [Fact]
    public async Task A_machine_without_the_platform_tool_falls_back_too()
    {
        _secretTool.Installed = false;

        (await SelectAsync(CredentialStoreChoice.Auto)).Describe().ShouldContain("encrypted file");
    }

    [Fact]
    public async Task A_platform_with_no_native_store_at_all_falls_back_quietly_enough_to_read()
    {
        var selector = new CredentialStoreSelector(null, new FileCredentialStore(_home.Directory));

        var store = await selector.SelectAsync(
            CredentialStoreChoice.Auto, _log, TestContext.Current.CancellationToken);

        store.Describe().ShouldContain("encrypted file");
        _log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_file_store_can_be_asked_for_where_a_native_one_is_available()
    {
        var store = await SelectAsync(CredentialStoreChoice.File);

        store.Describe().ShouldContain("encrypted file");

        // Nothing was fallen back from, so there is nothing to report.
        _log.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task Asking_for_the_native_store_where_it_cannot_be_reached_fails_rather_than_falling_back()
    {
        _secretTool.HasSecretService = false;

        var failure = await Should.ThrowAsync<ConfigurationException>(
            () => SelectAsync(CredentialStoreChoice.Native));

        failure.Message.ShouldContain("Secret Service");
        failure.Message.ShouldContain("--credential-store file");
    }

    private Task<ICredentialStore> SelectAsync(CredentialStoreChoice choice) =>
        Selector.SelectAsync(choice, _log, TestContext.Current.CancellationToken);
}
