using System.CommandLine;
using JiraServerMcp.Configuration;
using JiraServerMcp.Credentials;

namespace JiraServerMcp.Cli;

/// <summary>
/// The verbs this tool answers to. Help and parse errors go to standard error: standard output
/// belongs to the protocol (ADR-0002), and to whatever a verb was asked to print.
/// </summary>
internal static class VerbDispatcher
{
    public static async Task<int> RunAsync(string[] args)
    {
        var configuration = new InvocationConfiguration
        {
            Output = Console.Error,
            Error = Console.Error,

            // Off, so a ConfigurationException reaches the handler below instead of being
            // printed as "Unhandled exception" by System.CommandLine's own catch-all.
            EnableDefaultExceptionHandler = false,
        };

        try
        {
            return await Root().Parse(args).InvokeAsync(configuration, CancellationToken.None);
        }
        catch (ConfigurationException exception)
        {
            // A file this tool owns is unusable. That is the user's to fix, so they get the
            // sentence that tells them how rather than a stack trace.
            await Console.Error.WriteLineAsync(exception.Message);

            return 2;
        }
    }

    private static RootCommand Root()
    {
        var root = new RootCommand("An MCP server for self-hosted Jira Server.")
        {
            Serve(),
            ProfileCommand(),
            AuthCommand(),
        };

        return root;
    }

    private static Command Serve()
    {
        var profile = new Option<string>("--profile")
        {
            Description = "The profile to serve. One process serves exactly one profile.",
            Required = true,
        };

        var credentialStore = CredentialStoreOption();

        var serve = new Command("serve", "Serve the Model Context Protocol over stdio.")
        {
            profile,
            credentialStore,
        };

        serve.SetAction((parseResult, cancellationToken) => ServeVerb.RunAsync(
            parseResult.GetValue(profile)!,
            parseResult.GetValue(credentialStore),
            cancellationToken));

        return serve;
    }

    /// <summary>
    /// Which store to keep credentials in. Left alone, the operating system's own store is used
    /// where it can be reached and the encrypted file where it cannot.
    /// </summary>
    private static Option<CredentialStoreChoice> CredentialStoreOption() =>
        new("--credential-store")
        {
            Description = "Where credentials are kept: auto, native, or file.",
            DefaultValueFactory = _ => CredentialStoreChoice.Auto,
        };

    private static Command ProfileCommand()
    {
        var profile = new Command("profile", "Register and inspect Jira Servers.");

        var name = new Argument<string>("name")
        {
            Description = "The name this Jira Server is referred to by.",
        };

        var url = new Option<string>("--url")
        {
            Description = "The base URL of the Jira Server, including any context path.",
            Required = true,
        };

        var caBundle = new Option<string?>("--ca-bundle")
        {
            Description = "A certificate authority bundle to trust for this Jira Server.",
        };

        var add = new Command("add", "Register a Jira Server under a name.")
        {
            name,
            url,
            caBundle,
        };

        add.SetAction((parseResult, cancellationToken) => ProfileVerbs.AddAsync(
            parseResult.GetValue(name)!,
            parseResult.GetValue(url)!,
            parseResult.GetValue(caBundle),
            cancellationToken));

        var list = new Command("list", "List the registered profiles. Never prints a secret.");

        list.SetAction((_, cancellationToken) => ProfileVerbs.ListAsync(cancellationToken));

        var removeName = new Argument<string>("name")
        {
            Description = "The profile to remove, along with its credential.",
        };

        var removeCredentialStore = CredentialStoreOption();

        var remove = new Command("remove", "Remove a profile and its stored credential.")
        {
            removeName,
            removeCredentialStore,
        };

        remove.SetAction((parseResult, cancellationToken) => ProfileVerbs.RemoveAsync(
            parseResult.GetValue(removeName)!,
            parseResult.GetValue(removeCredentialStore),
            cancellationToken));

        profile.Subcommands.Add(add);
        profile.Subcommands.Add(list);
        profile.Subcommands.Add(remove);

        return profile;
    }

    private static Command AuthCommand()
    {
        var auth = new Command("auth", "Hand a personal access token to a profile.");

        // Declared so that passing it is an error with an explanation rather than "unknown
        // option", and hidden so that help never suggests it exists.
        var token = new Option<string?>("--token")
        {
            Description = "Not accepted; a token is never taken as an argument.",
            Recursive = true,
            Hidden = true,
        };

        auth.Options.Add(token);

        auth.Subcommands.Add(AuthSubcommand(
            "login",
            "Prompt for a personal access token, validate it against Jira, and store it.",
            token,
            AuthVerbs.LoginAsync));

        auth.Subcommands.Add(AuthSubcommand(
            "status",
            "Validate the stored credential and print the Jira user it resolves to.",
            token,
            AuthVerbs.StatusAsync));

        auth.Subcommands.Add(AuthSubcommand(
            "logout",
            "Delete the stored credential and leave the profile.",
            token,
            AuthVerbs.LogoutAsync));

        return auth;
    }

    private static Command AuthSubcommand(
        string verb,
        string description,
        Option<string?> token,
        Func<string, CredentialStoreChoice, CancellationToken, Task<int>> run)
    {
        var name = new Argument<string>("name")
        {
            Description = "The profile the token belongs to.",
        };

        var credentialStore = CredentialStoreOption();

        var command = new Command(verb, description)
        {
            name,
            credentialStore,
        };

        command.SetAction((parseResult, cancellationToken) =>
            parseResult.GetValue(token) is not null
                ? AuthVerbs.RefuseTokenArgumentAsync(parseResult.GetValue(name)!)
                : run(
                    parseResult.GetValue(name)!,
                    parseResult.GetValue(credentialStore),
                    cancellationToken));

        return command;
    }
}
