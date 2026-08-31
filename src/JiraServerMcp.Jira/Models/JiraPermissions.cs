using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// Jira's answer to "what may this account do here": every permission it knows, keyed by the
/// permission key an administrator sees on the permission-scheme screen, each carrying whether
/// this account holds it in the scope that was asked about.
/// </summary>
/// <remarks>
/// Internal, and unwrapped into a plain <c>key -&gt; bool</c> map before it leaves the client. The
/// envelope carries an identifier, a display name, a description and a type beside the one flag
/// that is wanted, and every one of those is either admin-renameable prose or a number nothing
/// reads. Jira Server 8 also answers with the whole enumeration whatever is asked for, so the map
/// is large and the filtering is this server's to do.
/// </remarks>
internal sealed record JiraMyPermissions(
    [property: JsonPropertyName("permissions")]
    IReadOnlyDictionary<string, JiraPermissionState>? Permissions);

internal sealed record JiraPermissionState(
    [property: JsonPropertyName("havePermission")] bool HavePermission);
