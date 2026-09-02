using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The README's claimed absences, held to the tool surface. A row cannot name the tool that would
/// falsify it — <c>(Issues, Delete)</c> is absent precisely because no delete tool exists to
/// reference — so the check is inverted: every registered tool declares the one pair it covers,
/// and a claimed absence is falsified by appearing in that coverage. A delete tool therefore has
/// to be added to <see cref="ToolSurface"/>, which forces its author to write the pair here, and
/// this suite fires on its own rather than waiting to be remembered.
///
/// <para>Two things this deliberately does not hold. The table's rows are its universe: a resource
/// nobody thought of is absent from the table and from the README together, and nothing here can
/// tell. And the README's mechanism claims — OAuth 1.0a, stdio, basic authentication, MCP
/// resources — are held by nothing, because no tool could ever falsify one; their absence from
/// this file is a decision, not an oversight.</para>
///
/// <para>The table lives here rather than in <c>src/</c> because nothing at runtime reads an
/// absence. That is the point of difference from <c>GrantSet</c>, whose table the runtime parses.
/// </para>
/// </summary>
public class ClaimedAbsenceTests
{
    private enum Resource
    {
        Account,
        Issues,
        Fields,
        Filters,
        Projects,
        Users,
        Boards,
        Sprints,
        Comments,
        Worklogs,
        Attachments,
        Links,
        RemoteLinks,
        Watchers,
        Votes,
    }

    /// <summary>
    /// <c>All</c> means no tool touches the resource in any direction — the strongest claim a row
    /// can make, which is why it is named rather than spelled as four rows. <c>Mutate</c> exists
    /// because Jira's sprint verbs do not decompose into the other four.
    /// </summary>
    private enum Action
    {
        Read,
        Add,
        Edit,
        Delete,
        Mutate,
        BulkWrite,
        All,
    }

    private sealed record Pair(Resource Resource, Action Action)
    {
        public override string ToString() => $"({Resource}, {Action})";
    }

    /// <summary>
    /// One claimed absence, and the phrase both README places name it by. Several rows share a
    /// phrase: "No delete tool of any kind, at any grant" is a stronger sentence for a reader than
    /// three weaker ones, and a failure names the row regardless of which bullet carries it.
    /// </summary>
    private sealed record Claimed(Resource Resource, Action Action, string Phrase)
    {
        public Pair Pair => new(Resource, Action);
    }

    private static readonly IReadOnlyList<Claimed> _claimed =
    [
        new(Resource.Issues, Action.Delete, "deletion"),
        new(Resource.Comments, Action.Delete, "deletion"),
        new(Resource.Worklogs, Action.Delete, "deletion"),
        new(Resource.Comments, Action.Edit, "comment and worklog editing"),
        new(Resource.Worklogs, Action.Edit, "comment and worklog editing"),
        new(Resource.Attachments, Action.Edit, "attachment replacement and deletion"),
        new(Resource.Attachments, Action.Delete, "attachment replacement and deletion"),
        new(Resource.Links, Action.Delete, "unlinking"),
        new(Resource.RemoteLinks, Action.Delete, "unlinking"),
        new(Resource.Sprints, Action.Mutate, "sprint mutation"),
        new(Resource.Watchers, Action.All, "watchers and votes"),
        new(Resource.Votes, Action.All, "watchers and votes"),
        new(Resource.Issues, Action.BulkWrite, "bulk writes"),
    ];

    /// <summary>
    /// What each registered tool covers. Many-to-one: <c>jira_transition_issue</c> and
    /// <c>jira_update_issue</c> both edit an issue.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, Pair> _coverage = new Dictionary<Type, Pair>
    {
        [typeof(WhoamiTool)] = new(Resource.Account, Action.Read),
        [typeof(SearchTool)] = new(Resource.Issues, Action.Read),
        [typeof(GetJqlFieldsTool)] = new(Resource.Fields, Action.Read),
        [typeof(ListSavedFiltersTool)] = new(Resource.Filters, Action.Read),
        [typeof(MyOpenIssuesTool)] = new(Resource.Issues, Action.Read),
        [typeof(ChangedSinceTool)] = new(Resource.Issues, Action.Read),
        [typeof(GetIssuesTool)] = new(Resource.Issues, Action.Read),
        [typeof(GetAttachmentTool)] = new(Resource.Attachments, Action.Read),
        [typeof(ListProjectsTool)] = new(Resource.Projects, Action.Read),
        [typeof(GetProjectTool)] = new(Resource.Projects, Action.Read),
        [typeof(GetCreateFieldsTool)] = new(Resource.Fields, Action.Read),
        [typeof(GetEditFieldsTool)] = new(Resource.Fields, Action.Read),
        [typeof(SearchUsersTool)] = new(Resource.Users, Action.Read),
        [typeof(ListBoardsTool)] = new(Resource.Boards, Action.Read),
        [typeof(ListSprintsTool)] = new(Resource.Sprints, Action.Read),
        [typeof(GetSprintIssuesTool)] = new(Resource.Sprints, Action.Read),
        [typeof(GetBacklogTool)] = new(Resource.Boards, Action.Read),
        [typeof(CreateIssueTool)] = new(Resource.Issues, Action.Add),
        [typeof(UpdateIssueTool)] = new(Resource.Issues, Action.Edit),
        [typeof(TransitionIssueTool)] = new(Resource.Issues, Action.Edit),
        [typeof(AddCommentTool)] = new(Resource.Comments, Action.Add),
        [typeof(AddWorklogTool)] = new(Resource.Worklogs, Action.Add),
        [typeof(LinkIssuesTool)] = new(Resource.Links, Action.Add),
        [typeof(AddRemoteLinkTool)] = new(Resource.RemoteLinks, Action.Add),
        [typeof(AddAttachmentTool)] = new(Resource.Attachments, Action.Add),
    };

    private static readonly string _readme =
        File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, "README.md"));

    [Fact]
    public void Every_registered_tool_declares_exactly_one_pair()
    {
        // This is the forcing function: the tool that would falsify a row cannot be registered
        // without its author writing the pair that falsifies it.
        _coverage.Keys.Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(ToolSurface.Entries
                .Select(entry => entry.ToolType.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void No_claimed_absence_is_covered_by_a_registered_tool()
    {
        foreach (var claimed in _claimed)
        {
            var falsifying = _coverage
                .Where(tool => claimed.Action is Action.All
                    ? tool.Value.Resource == claimed.Resource
                    : tool.Value == claimed.Pair)
                .Select(tool => tool.Key.Name)
                .ToList();

            falsifying.ShouldBeEmpty(
                $"The README claims {claimed.Pair} is absent — '{claimed.Phrase}' — but "
                + $"{string.Join(", ", falsifying)} covers it. Either the tool is registered and "
                + "the claim is false, or the pair it declares is wrong.");
        }
    }

    [Fact]
    public void Every_phrase_the_deliberately_absent_sentence_names_has_a_row()
    {
        Sentence().OrderBy(phrase => phrase, StringComparer.Ordinal)
            .ShouldBe(_claimed
                .Select(claimed => claimed.Phrase)
                .Distinct()
                .OrderBy(phrase => phrase, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_rows_phrase_appears_in_both_readme_places()
    {
        var sentence = Sentence();
        var limitations = Limitations();

        foreach (var phrase in _claimed.Select(claimed => claimed.Phrase).Distinct())
        {
            sentence.ShouldContain(
                phrase,
                $"The 'Deliberately absent' sentence no longer names '{phrase}'.");

            var bullet = $"- **{char.ToUpperInvariant(phrase[0]) + phrase[1..]}.**";

            limitations.ShouldContain(
                bullet,
                Case.Sensitive,
                $"'Known limitations' has no '{bullet}' bullet.");
        }
    }

    /// <summary>
    /// The phrases the one-liner under the tool catalogue names. It is the machine-readable place
    /// because it carries only claims a tool could falsify — the mechanism claims live in
    /// 'Known limitations' and would need an exemption here.
    /// </summary>
    private static IReadOnlyList<string> Sentence()
    {
        const string opening = "Deliberately absent:";

        var start = _readme.IndexOf(opening, StringComparison.Ordinal);

        start.ShouldBeGreaterThanOrEqualTo(0, $"The README no longer says '{opening}'.");

        start += opening.Length;

        var end = _readme.IndexOf('.', start);

        return
        [
            .. _readme[start..end]
                .ReplaceLineEndings(" ")
                .Split(',')
                .Select(phrase => phrase.Trim())
                .Select(phrase => phrase.StartsWith("and ", StringComparison.Ordinal)
                    ? phrase["and ".Length..]
                    : phrase),
        ];
    }

    private static string Limitations()
    {
        const string heading = "## Known limitations";

        var start = _readme.IndexOf(heading, StringComparison.Ordinal);

        start.ShouldBeGreaterThanOrEqualTo(0, $"The README has no '{heading}' section.");

        var next = _readme.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);

        return next < 0 ? _readme[start..] : _readme[start..next];
    }
}
