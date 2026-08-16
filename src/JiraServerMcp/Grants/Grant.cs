namespace JiraServerMcp.Grants;

/// <summary>
/// A named category of write permission the operator hands to one MCP client. Without its grant a
/// tool is not registered at all, so an agent cannot attempt it.
/// </summary>
internal enum Grant
{
    IssuesWrite,
    CommentsWrite,
    WorklogsWrite,
}
