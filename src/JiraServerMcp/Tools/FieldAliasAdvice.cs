using JiraServerMcp.Profiles;

namespace JiraServerMcp.Tools;

/// <summary>
/// What a write rejected over a field should add when this profile declares aliases. A field name
/// this server does not recognise is passed to Jira unchanged — the field catalogue lives there,
/// not here — so the moment an unknown name fails loudly is the moment Jira refuses it, and that
/// is where the aliases it could have used belong.
/// </summary>
internal static class FieldAliasAdvice
{
    public static string From(FieldAliases aliases) =>
        aliases.Any
            ? $" This profile also declares these field aliases, either name being accepted: "
              + $"{string.Join(", ", aliases.Known)}."
            : "";
}
