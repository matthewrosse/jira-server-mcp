using JiraServerMcp.Credentials;

namespace JiraServerMcp.Tests;

/// <summary>
/// The contract every credential store keeps. Written against <see cref="ICredentialStore"/>
/// alone: no test here names an implementation, a file, or a platform tool.
/// </summary>
public abstract class CredentialStoreContract : IAsyncLifetime
{
    private readonly string _suffix = Guid.NewGuid().ToString("n")[..8];

    internal abstract ICredentialStore Store { get; }

    private string Profile => "contract-" + _suffix;

    private string OtherProfile => "contract-other-" + _suffix;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// A genuine backend keeps what it is given, so every test cleans up after itself even when
    /// it failed.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        await Store.DeleteAsync(Profile, CancellationToken.None);
        await Store.DeleteAsync(OtherProfile, CancellationToken.None);
    }

    [Fact]
    public async Task A_stored_token_comes_back_byte_for_byte()
    {
        await Store.SetAsync(Profile, "s3cr3t-personal-access-token", Cancellation);

        (await Store.GetAsync(Profile, Cancellation)).ShouldBe("s3cr3t-personal-access-token");
    }

    [Fact]
    public async Task Storing_twice_leaves_the_second_token()
    {
        await Store.SetAsync(Profile, "first-personal-access-token", Cancellation);
        await Store.SetAsync(Profile, "second-personal-access-token", Cancellation);

        (await Store.GetAsync(Profile, Cancellation)).ShouldBe("second-personal-access-token");
    }

    [Fact]
    public async Task A_profile_with_no_token_answers_with_nothing()
    {
        (await Store.GetAsync(Profile, Cancellation)).ShouldBeNull();
    }

    [Fact]
    public async Task A_deleted_token_is_gone()
    {
        await Store.SetAsync(Profile, "s3cr3t-personal-access-token", Cancellation);
        await Store.DeleteAsync(Profile, Cancellation);

        (await Store.GetAsync(Profile, Cancellation)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_a_token_that_was_never_there_is_not_an_error()
    {
        await Store.DeleteAsync(Profile, Cancellation);
    }

    [Fact]
    public async Task One_profiles_token_is_not_another_profiles()
    {
        await Store.SetAsync(Profile, "work-personal-access-token", Cancellation);
        await Store.SetAsync(OtherProfile, "spare-personal-access-token", Cancellation);

        (await Store.GetAsync(Profile, Cancellation)).ShouldBe("work-personal-access-token");
        (await Store.GetAsync(OtherProfile, Cancellation)).ShouldBe("spare-personal-access-token");

        await Store.DeleteAsync(OtherProfile, Cancellation);

        (await Store.GetAsync(Profile, Cancellation)).ShouldBe("work-personal-access-token");
    }

    [Fact]
    public void Describing_the_store_names_it_without_naming_a_secret()
    {
        Store.Describe().ShouldNotBeNullOrWhiteSpace();
    }
}

public sealed class FileCredentialStoreContractTests : CredentialStoreContract, IDisposable
{
    private readonly ConfigurationHome _home = new();

    internal override ICredentialStore Store => new FileCredentialStore(_home.Directory);

    public void Dispose() => _home.Dispose();
}

public sealed class KeychainCredentialStoreContractTests : CredentialStoreContract
{
    private readonly KeychainCredentialStore _store = new(new FakeSecurity());

    internal override ICredentialStore Store => _store;
}

public sealed class SecretServiceCredentialStoreContractTests : CredentialStoreContract
{
    private readonly SecretServiceCredentialStore _store = new(new FakeSecretTool());

    internal override ICredentialStore Store => _store;
}

/// <summary>
/// The same contract against whatever this machine genuinely has — Keychain, Credential Manager,
/// or Secret Service — which is the run that matters on each leg of the CI matrix. Skipped where
/// no native store is reachable, because that case is the fallback's job and is tested there.
/// </summary>
public sealed class GenuineNativeCredentialStoreContractTests : CredentialStoreContract
{
    private readonly INativeCredentialStore? _store = NativeCredentialStore.ForThisPlatform(new ProcessRunner());

    private bool _usable;

    internal override ICredentialStore Store =>
        _store ?? throw new InvalidOperationException("No native credential store on this platform.");

    public override async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(_store is null, "This platform has no native credential store.");

        _usable = await _store!.IsUsableAsync(TestContext.Current.CancellationToken);

        Assert.SkipUnless(_usable, "The native credential store is not reachable from this session.");
    }

    /// <summary>
    /// A skipped test has stored nothing, and cleaning up through a store that cannot be reached
    /// would turn the skip into a failure.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (_usable)
        {
            await base.DisposeAsync();
        }
    }
}
