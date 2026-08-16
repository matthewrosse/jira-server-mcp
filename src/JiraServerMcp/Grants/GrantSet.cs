using JiraServerMcp.Configuration;

namespace JiraServerMcp.Grants;

/// <summary>
/// The grants one server process was launched with. Parsed once, at startup, so a name nobody
/// recognises is a refusal to start rather than a tool that silently never appears.
/// </summary>
internal sealed class GrantSet
{
    private static readonly IReadOnlyDictionary<string, Grant> Names =
        new Dictionary<string, Grant>(StringComparer.OrdinalIgnoreCase)
        {
            ["issues:write"] = Grant.IssuesWrite,
            ["comments:write"] = Grant.CommentsWrite,
            ["worklogs:write"] = Grant.WorklogsWrite,
        };

    private readonly HashSet<Grant> _granted;

    private GrantSet(HashSet<Grant> granted) => _granted = granted;

    /// <summary>
    /// The grants named by every <c>--allow</c> argument, each of which may itself carry several
    /// names separated by commas.
    /// </summary>
    /// <exception cref="ConfigurationException">A name is not one of the three.</exception>
    public static GrantSet Parse(IReadOnlyList<string> allowed)
    {
        var granted = new HashSet<Grant>();

        foreach (var name in allowed.SelectMany(argument => argument.Split(',')))
        {
            granted.Add(Named(name.Trim()));
        }

        return new GrantSet(granted);
    }

    public bool Allows(Grant grant) => _granted.Contains(grant);

    private static Grant Named(string name) =>
        Names.TryGetValue(name, out var grant)
            ? grant
            : throw new ConfigurationException(
                $"'{name}' is not a grant this server knows. The grants are "
                + $"{string.Join(", ", Names.Keys)}. They are given at launch, as "
                + "'--allow issues:write,comments:write'.");
}
