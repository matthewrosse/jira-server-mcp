using System.CommandLine;

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
        };

        return await Root().Parse(args).InvokeAsync(configuration, CancellationToken.None);
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

        var serve = new Command("serve", "Serve the Model Context Protocol over stdio.")
        {
            profile,
        };

        serve.SetAction((parseResult, cancellationToken) =>
            ServeVerb.RunAsync(parseResult.GetValue(profile)!, cancellationToken));

        return serve;
    }

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

        var remove = new Command("remove", "Remove a profile and its stored credential.")
        {
            removeName,
        };

        remove.SetAction((parseResult, cancellationToken) =>
            ProfileVerbs.RemoveAsync(parseResult.GetValue(removeName)!, cancellationToken));

        profile.Subcommands.Add(add);
        profile.Subcommands.Add(list);
        profile.Subcommands.Add(remove);

        return profile;
    }

    private static Command AuthCommand()
    {
        var auth = new Command("auth", "Hand a personal access token to a profile.");

        var name = new Argument<string>("name")
        {
            Description = "The profile the token belongs to.",
        };

        var login = new Command("login", "Read a personal access token from standard input and store it.")
        {
            name,
        };

        login.SetAction((parseResult, cancellationToken) =>
            AuthVerbs.LoginAsync(parseResult.GetValue(name)!, cancellationToken));

        auth.Subcommands.Add(login);

        return auth;
    }
}
