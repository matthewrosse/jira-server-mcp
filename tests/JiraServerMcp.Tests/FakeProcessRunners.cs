using JiraServerMcp.Credentials;

namespace JiraServerMcp.Tests;

/// <summary>
/// `security` as macOS implements it, down to the exit code for a missing item and the two
/// prompts `add-generic-password -w` reads from standard input. The password never appears in
/// an argument list, which the fake insists on rather than assumes.
/// </summary>
internal sealed class FakeSecurity : IProcessRunner
{
    private const int ItemNotFound = 44;

    private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

    public bool Installed { get; set; } = true;

    public bool KeychainReadable { get; set; } = true;

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        fileName.ShouldBe("security");

        if (!Installed)
        {
            return Task.FromResult(ProcessResult.NotInstalled);
        }

        return Task.FromResult(arguments[0] switch
        {
            "list-keychains" => KeychainReadable
                ? new ProcessResult(true, 0, "\"/Users/someone/Library/Keychains/login.keychain-db\"\n", "")
                : new ProcessResult(true, 1, "", "security: SecKeychainCopySearchList: User interaction is not allowed.\n"),
            "find-generic-password" => _items.TryGetValue(Account(arguments), out var token)
                ? new ProcessResult(true, 0, token + "\n", "")
                : NotFound,
            "add-generic-password" => Add(arguments, standardInput),
            "delete-generic-password" => _items.Remove(Account(arguments))
                ? new ProcessResult(true, 0, "", "")
                : NotFound,
            var verb => throw new InvalidOperationException($"Unexpected security verb '{verb}'."),
        });
    }

    private static ProcessResult NotFound => new(
        true,
        ItemNotFound,
        "",
        "security: SecKeychainSearchCopyNext: The specified item could not be found in the keychain.\n");

    private ProcessResult Add(IReadOnlyList<string> arguments, string? standardInput)
    {
        // -w last and empty is the whole point: a value here would put the token in an argument
        // list every process on the machine can read.
        arguments[^1].ShouldBe("-w");

        var lines = (standardInput ?? "").Split('\n');

        lines.Length.ShouldBeGreaterThanOrEqualTo(2);
        lines[0].ShouldBe(lines[1]);

        _items[Account(arguments)] = lines[0];

        return new ProcessResult(true, 0, "", "");
    }

    private static string Account(IReadOnlyList<string> arguments)
    {
        arguments.ShouldContain("jira-server-mcp");

        var account = arguments.SkipWhile(argument => argument != "-a").Skip(1).FirstOrDefault();

        return account.ShouldNotBeNull();
    }
}

/// <summary>
/// `secret-tool` as libsecret implements it. Exit code 1 means both "no such secret" and "no
/// Secret Service on this machine"; the difference is on standard error, which is what
/// <see cref="HasSecretService"/> switches between.
/// </summary>
internal sealed class FakeSecretTool : IProcessRunner
{
    private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

    public bool Installed { get; set; } = true;

    public bool HasSecretService { get; set; } = true;

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        fileName.ShouldBe("secret-tool");

        if (!Installed)
        {
            return Task.FromResult(ProcessResult.NotInstalled);
        }

        if (!HasSecretService)
        {
            return Task.FromResult(new ProcessResult(
                true,
                1,
                "",
                "secret-tool: The name org.freedesktop.secrets was not provided by any .service files\n"));
        }

        return Task.FromResult(arguments[0] switch
        {
            "lookup" => _items.TryGetValue(Account(arguments), out var token)
                ? new ProcessResult(true, 0, token, "")
                : new ProcessResult(true, 1, "", ""),
            "store" => Store(arguments, standardInput),
            "clear" => Clear(arguments),
            var verb => throw new InvalidOperationException($"Unexpected secret-tool verb '{verb}'."),
        });
    }

    private ProcessResult Store(IReadOnlyList<string> arguments, string? standardInput)
    {
        // secret-tool takes the secret on standard input and stores every byte of it, trailing
        // newline included, so the store must not add one.
        var secret = standardInput.ShouldNotBeNull();

        secret.ShouldNotEndWith("\n");

        _items[Account(arguments)] = secret;

        return new ProcessResult(true, 0, "", "");
    }

    private ProcessResult Clear(IReadOnlyList<string> arguments)
    {
        // A clear that matched nothing is exit 1 with nothing on either stream.
        return _items.Remove(Account(arguments))
            ? new ProcessResult(true, 0, "", "")
            : new ProcessResult(true, 1, "", "");
    }

    private static string Account(IReadOnlyList<string> arguments)
    {
        arguments.ShouldContain("jira-server-mcp");

        var account = arguments.SkipWhile(argument => argument != "account").Skip(1).FirstOrDefault();

        return account.ShouldNotBeNull();
    }
}
