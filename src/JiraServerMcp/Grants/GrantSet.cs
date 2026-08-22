using JiraServerMcp.Configuration;

namespace JiraServerMcp.Grants;

/// <summary>
/// The grant vocabulary and the grants one server process was launched with. The table below is
/// the only place a grant's name is enumerated: the <c>--allow</c> help text, the refusal message
/// and the README test's mapping all read it rather than keeping a list that goes stale. Prose
/// that names one grant — a tool's docstring, the README — still writes it out, and is held to the
/// table by the README test. Parsed once, at startup, so a name nobody recognises is a refusal to
/// start rather than a tool that silently never appears.
/// </summary>
internal sealed class GrantSet
{
    private static readonly IReadOnlyDictionary<string, Grant> _names =
        new Dictionary<string, Grant>(StringComparer.OrdinalIgnoreCase)
        {
            ["issues:write"] = Grant.IssuesWrite,
            ["comments:write"] = Grant.CommentsWrite,
            ["worklogs:write"] = Grant.WorklogsWrite,
            ["links:write"] = Grant.LinksWrite,
        };

    /// <summary>
    /// Every grant's name, in the enum's declaration order rather than the table's unspecified
    /// enumeration order, so the help text and the refusal message read the same way twice.
    /// </summary>
    public static readonly IReadOnlyList<string> Names = [.. Enum.GetValues<Grant>().Select(Name)];

    private readonly HashSet<Grant> _granted;

    private GrantSet(HashSet<Grant> granted) => _granted = granted;

    /// <summary>
    /// The grants named by every <c>--allow</c> argument, each of which may itself carry several
    /// names separated by commas.
    /// </summary>
    /// <exception cref="ConfigurationException">A name is not one this server knows.</exception>
    public static GrantSet Parse(IReadOnlyList<string> allowed)
    {
        var granted = new HashSet<Grant>();

        foreach (var name in allowed.SelectMany(argument => argument.Split(',')))
        {
            granted.Add(Named(name.Trim()));
        }

        return new GrantSet(granted);
    }

    /// <summary>The name the operator writes for a grant.</summary>
    /// <exception cref="InvalidOperationException">The grant has no row in the table.</exception>
    public static string Name(Grant grant) =>
        _names.FirstOrDefault(row => row.Value == grant).Key
        ?? throw new InvalidOperationException(
            $"Grant.{grant} has no name in this server's grant table. A new grant needs a row "
            + "there, which is what the help text, the refusal message and the README's "
            + "catalogue all read.");

    public bool Allows(Grant grant) => _granted.Contains(grant);

    private static Grant Named(string name) =>
        _names.TryGetValue(name, out var grant)
            ? grant
            : throw new ConfigurationException(
                $"'{name}' is not a grant this server knows. The grants are "
                + $"{string.Join(", ", Names)}. They are given at launch, as "
                + "'--allow issues:write,comments:write'.");
}
