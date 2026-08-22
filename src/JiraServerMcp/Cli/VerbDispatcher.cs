using System.CommandLine;
using JiraServerMcp.Configuration;
using JiraServerMcp.Credentials;
using JiraServerMcp.Grants;

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

        // ADR-0005: what this client may write is decided here, in the configuration file its
        // operator already reads, and never by anything a tool call carries.
        var allow = new Option<string[]>("--allow")
        {
            Description =
                $"Write categories this client is granted: {string.Join(", ", GrantSet.Names)}. "
                + "Repeatable, or separated by commas. Tools without their grant are not "
                + "registered.",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [],
        };

        var credentialStore = CredentialStoreOption();

        var serve = new Command("serve", "Serve the Model Context Protocol over stdio.")
        {
            profile,
            allow,
            credentialStore,
        };

        serve.SetAction((parseResult, cancellationToken) => ServeVerb.RunAsync(
            parseResult.GetValue(profile)!,
            parseResult.GetValue(allow)!,
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

        var refreshName = new Argument<string>("name")
        {
            Description = "The profile whose capability probe is taken again.",
        };

        var refreshCredentialStore = CredentialStoreOption();

        var refresh = new Command(
            "refresh",
            "Take this profile's capability probe again: version, deployment type, and whether "
            + "Jira Software is licensed.")
        {
            refreshName,
            refreshCredentialStore,
        };

        refresh.SetAction((parseResult, cancellationToken) => ProfileVerbs.RefreshAsync(
            parseResult.GetValue(refreshName)!,
            parseResult.GetValue(refreshCredentialStore),
            cancellationToken));

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

        profile.Subcommands.Add(AliasCommand());
        profile.Subcommands.Add(QueryCommand());
        profile.Subcommands.Add(add);
        profile.Subcommands.Add(list);
        profile.Subcommands.Add(refresh);
        profile.Subcommands.Add(remove);

        return profile;
    }

    /// <summary>
    /// `profile alias set|list|remove`. Aliases live on the profile because that is where this
    /// project keeps configuration (ADR-0005); a second location would need precedence rules of
    /// its own.
    /// </summary>
    private static Command AliasCommand()
    {
        var alias = new Command("alias", "Declare your own names for this Jira's fields.");

        var setProfile = new Argument<string>("profile")
        {
            Description = "The profile the alias belongs to.",
        };

        var setAlias = new Argument<string>("alias")
        {
            Description = "The name to read and write the field by, such as \"story_points\".",
        };

        var setField = new Argument<string>("field")
        {
            Description = "Jira's own identifier for the field, such as \"customfield_10010\".",
        };

        var set = new Command("set", "Declare an alias for a field, or replace one.")
        {
            setProfile,
            setAlias,
            setField,
        };

        set.SetAction((parseResult, _) => ProfileVerbs.SetAliasAsync(
            parseResult.GetValue(setProfile)!,
            parseResult.GetValue(setAlias)!,
            parseResult.GetValue(setField)!));

        var listProfile = new Argument<string>("profile")
        {
            Description = "The profile whose aliases are listed.",
        };

        var list = new Command("list", "List a profile's field aliases.") { listProfile };

        list.SetAction((parseResult, _) => ProfileVerbs.ListAliasesAsync(
            parseResult.GetValue(listProfile)!));

        var removeProfile = new Argument<string>("profile")
        {
            Description = "The profile the alias belongs to.",
        };

        var removeAlias = new Argument<string>("alias")
        {
            Description = "The alias to remove. The field itself is untouched.",
        };

        var remove = new Command("remove", "Remove a field alias.")
        {
            removeProfile,
            removeAlias,
        };

        remove.SetAction((parseResult, _) => ProfileVerbs.RemoveAliasAsync(
            parseResult.GetValue(removeProfile)!,
            parseResult.GetValue(removeAlias)!));

        alias.Subcommands.Add(set);
        alias.Subcommands.Add(list);
        alias.Subcommands.Add(remove);

        return alias;
    }

    /// <summary>
    /// `profile query add|list|remove`. A declared query becomes a tool of its own, under a fixed
    /// prefix so an operator's name can never collide with a built-in one.
    /// </summary>
    private static Command QueryCommand()
    {
        var query = new Command("query", "Declare canned queries this deployment offers as tools.");

        var addProfile = new Argument<string>("profile")
        {
            Description = "The profile the query belongs to.",
        };

        var addName = new Argument<string>("query")
        {
            Description = "The query's name. The tool is called jira_q_<query>.",
        };

        var jql = new Option<string>("--jql")
        {
            Description = "The JQL to run. Checked against Jira before it is stored.",
            Required = true,
        };

        var description = new Option<string>("--description")
        {
            Description = "What the query is for. An agent reads this when choosing a tool.",
            Required = true,
        };

        var addCredentialStore = CredentialStoreOption();

        var add = new Command("add", "Declare a query, or replace one of the same name.")
        {
            addProfile,
            addName,
            jql,
            description,
            addCredentialStore,
        };

        add.SetAction((parseResult, cancellationToken) => ProfileVerbs.AddQueryAsync(
            parseResult.GetValue(addProfile)!,
            parseResult.GetValue(addName)!,
            parseResult.GetValue(jql)!,
            parseResult.GetValue(description)!,
            parseResult.GetValue(addCredentialStore),
            cancellationToken));

        var listProfile = new Argument<string>("profile")
        {
            Description = "The profile whose queries are listed.",
        };

        var list = new Command("list", "List a profile's canned queries.") { listProfile };

        list.SetAction((parseResult, _) => ProfileVerbs.ListQueriesAsync(
            parseResult.GetValue(listProfile)!));

        var removeProfile = new Argument<string>("profile")
        {
            Description = "The profile the query belongs to.",
        };

        var removeName = new Argument<string>("query")
        {
            Description = "The query to remove.",
        };

        var remove = new Command("remove", "Remove a canned query.")
        {
            removeProfile,
            removeName,
        };

        remove.SetAction((parseResult, _) => ProfileVerbs.RemoveQueryAsync(
            parseResult.GetValue(removeProfile)!,
            parseResult.GetValue(removeName)!));

        query.Subcommands.Add(add);
        query.Subcommands.Add(list);
        query.Subcommands.Add(remove);

        return query;
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
