using System.Text.Json;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// One issue read on its own, with whichever expansions were asked for. Unlike the issues a search
/// returns, this carries sections beside the field projection — and each of those has a shape Jira
/// fixes, so they are read into records here rather than left as JSON for a caller to pick apart.
/// </summary>
/// <param name="Key">The issue key, as Jira spells it.</param>
/// <param name="Fields">
/// The field projection, minus the three collection fields that became sections of their own.
/// Still JSON, because the projection is open to any custom field.
/// </param>
/// <param name="Transitions">What this account can move the issue to, empty unless expanded.</param>
/// <param name="Changelog">The issue history, null unless expanded.</param>
/// <param name="Comments">The issue's comments, null unless expanded.</param>
/// <param name="Links">Links to other issues, empty unless expanded.</param>
/// <param name="RemoteLinks">
/// Links out of Jira, null unless they were asked for and could be read. Remote links are not a
/// field on the issue, so they cost a request of their own, and one this account may be refused
/// even where the issue itself was readable.
/// </param>
/// <param name="Worklogs">Logged time, null unless expanded.</param>
/// <param name="Attachments">The files on the issue, empty unless expanded.</param>
public sealed record JiraIssueDetail(
    string Key,
    IReadOnlyDictionary<string, JsonElement> Fields,
    IReadOnlyList<JiraTransition> Transitions,
    JiraChangelog? Changelog,
    JiraComments? Comments,
    IReadOnlyList<JiraIssueLink> Links,
    IReadOnlyList<JiraRemoteLink>? RemoteLinks,
    JiraWorklogs? Worklogs,
    IReadOnlyList<JiraAttachment> Attachments)
{
    /// <summary>The status id, which survives an admin renaming the workflow.</summary>
    public string? StatusId => JiraFields.StatusId(Fields);

    /// <summary>The status name, which is the field "is this still open?" turns on.</summary>
    public string? Status => JiraFields.StatusName(Fields);

    public string? TypeName => JiraFields.TypeName(Fields);
}

/// <summary>
/// A transition this account can make on this issue right now, and what its screen will ask for
/// if it does.
/// </summary>
public sealed record JiraTransition(
    string Id,
    string Name,
    string? ToStatus,
    IReadOnlyList<JiraTransitionField> Fields);

/// <summary>One field on a transition screen. <paramref name="Id"/> is what a write must send.</summary>
public sealed record JiraTransitionField(string Id, string Name, bool Required);

/// <summary>
/// The issue's comments. <see cref="Total"/> is Jira's own count, which is what says whether the
/// comments in hand are all of them.
/// </summary>
public sealed record JiraComments(int Total, IReadOnlyList<JiraComment> Comments);

/// <summary>One comment. The body is Jira wiki markup, passed through as Jira wrote it.</summary>
public sealed record JiraComment(string? Author, string? Created, string? Body);

/// <summary>The issue's history, oldest group first, as Jira orders it.</summary>
public sealed record JiraChangelog(int Total, IReadOnlyList<JiraChangeGroup> Histories);

/// <summary>Everything one person changed in one edit.</summary>
public sealed record JiraChangeGroup(
    string? Author,
    string? Created,
    IReadOnlyList<JiraChangeItem> Items);

/// <summary>One field's move within a change group.</summary>
public sealed record JiraChangeItem(string Field, string? From, string? To);

/// <summary>
/// A link to another issue.
/// </summary>
/// <param name="Relation">
/// Jira's own wording for this end of the link — "blocks" against "is blocked by". It carries the
/// link type and the direction together, which is the only form in which the direction means
/// anything.
/// </param>
/// <param name="Key">The issue on the other end of the link.</param>
/// <param name="Summary">That issue's summary, where the projection carried one.</param>
public sealed record JiraIssueLink(string Relation, string Key, string? Summary);

/// <summary>
/// A link from an issue to a URL outside Jira.
/// </summary>
/// <param name="Title">The text the link panel shows.</param>
/// <param name="Url">
/// Where the link points, which is also what identifies it: this server sends the URL as the
/// link's <c>globalId</c>, so one URL is one link on one issue.
/// </param>
/// <param name="Relationship">
/// Jira's free-text grouping header in the link panel — "pull request" — which a producer may
/// leave unset.
/// </param>
public sealed record JiraRemoteLink(string Title, string Url, string? Relationship);

/// <summary>
/// One type of issue link, named once from each end. The two phrases are what this server's tools
/// take in place of a type name and a direction, because only the phrase says which end is which.
/// </summary>
/// <param name="Name">Jira's name for the type, which is what a write must send.</param>
/// <param name="Inward">The wording for the end the link points at — "is blocked by".</param>
/// <param name="Outward">The wording for the end the link points from — "blocks".</param>
public sealed record JiraIssueLinkType(string Name, string Inward, string Outward);

/// <summary>
/// One file on an issue. On a legacy Jira the specification is regularly the attachment rather
/// than the description, so this is what tells an agent whether there is one worth reading and
/// which identifier to read it by.
/// </summary>
/// <param name="Id">What a fetch must send. Jira's own, opaque, and unique across the instance.</param>
/// <param name="FileName">The name whoever uploaded it chose, which is all an agent has to go on.</param>
/// <param name="Size">The file's size in bytes, as Jira records it.</param>
/// <param name="MimeType">
/// The media type Jira claims. Advisory only: legacy instances report it wrongly often enough that
/// nothing may branch on it, and whether the bytes are readable is decided by reading them.
/// </param>
/// <param name="Content">
/// Where Jira serves the bytes. An absolute URL Jira composes from its own configured base, which
/// is not always the base this server was pointed at.
/// </param>
public sealed record JiraAttachment(
    string Id,
    string FileName,
    long Size,
    string? MimeType,
    string? Content);

/// <summary>The issue's logged time. <see cref="Total"/> is Jira's own count.</summary>
public sealed record JiraWorklogs(int Total, IReadOnlyList<JiraWorklog> Worklogs);

/// <summary>One logged entry. <paramref name="TimeSpent"/> is Jira's own form, such as "3h 30m".</summary>
public sealed record JiraWorklog(string? Author, string? TimeSpent, string? Started);
