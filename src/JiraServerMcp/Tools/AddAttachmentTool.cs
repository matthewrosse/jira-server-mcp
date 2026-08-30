using System.ComponentModel;
using JiraServerMcp.Jira;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>attachments:write</c> grant (ADR-0005). An attachment is the one
/// write that ships a blob a reviewer cannot skim in the issue history, so an operator may
/// reasonably want every other write on and this off.
/// </summary>
/// <remarks>
/// The content crosses the boundary as a string rather than as a path, so this server opens no file
/// on the machine it runs on and the path-traversal question never arises (ADR-0012). What it costs
/// is that an artefact already on the agent's disk is paid for twice — read in, echoed out — which
/// is why the size is capped and the file name is validated rather than trusted.
///
/// One file per call. Jira's endpoint takes several in one body, and taking a list would multiply
/// the failure modes — partial success, per-file accounting, which ones landed after a timeout —
/// for a case nobody has.
/// </remarks>
[McpServerToolType]
internal sealed class AddAttachmentTool(
    JiraClient jira,
    ServedProfile profile,
    WriteAttempts attempts)
{
    private const string Name = "jira_add_attachment";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(AddedAttachmentOutput))]
    [Description(
        "Attach one text file to an issue. The content is sent as text, not as a path — this "
        + "server opens no file on the machine it runs on — and is stored as text/plain whatever "
        + "the file is named, so an attachment is bytes rather than Jira wiki markup. Use it for "
        + "an artefact worth keeping off the issue's prose: a test log, a diff, a report. It is "
        + "read back with jira_get_attachment. " + AttachmentUpload.ContentLimit
        + ", and Jira appends rather than replaces, so the same file sent twice is two "
        + "attachments. Nothing here edits or removes one.")]
    public async Task<CallToolResult> AddAttachmentAsync(
        [Description("The issue key, such as \"PROJ-42\".")]
        string key,
        [Description(
            "The name the file is stored under — a label a person reads, never a path. No "
            + "slashes, and no control characters.")]
        string fileName,
        [Description("The file's text. Stored verbatim, and never echoed back.")]
        string content,
        [Description(RetrySafeWrite.KeyDescription)]
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        // Before the key is claimed: a call this server refuses outright must not spend one.
        if (AttachmentUpload.Refuse(key, fileName, content) is { } refusal)
        {
            return ToolCall.Error(refusal);
        }

        return await RetrySafeWrite.RunAsync(
            attempts,
            Name,
            idempotencyKey,
            noun: "attachment",
            howToCheck:
                "Read the issue with jira_get_issues and the attachments expansion before sending "
                + "it again under a new key.",
            profile,
            $"attaching {fileName} to {key}",
            whenUnreachable: $", and nothing was attached to {key}",
            whenTimedOut:
                $". The upload was sent once and was not repeated, so read {key} with "
                + "jira_get_issues and the attachments expansion before sending it again — Jira "
                + "appends an attachment rather than replacing one, so a blind retry is a second "
                + "copy of the file.",
            async () =>
            {
                var added = await jira.AddAttachmentAsync(key, fileName, content, cancellationToken);

                // The caller wrote the content; handing it back would be context spent on nothing.
                var rendered = new Rendered(
                    $"Attached {added.FileName} to {key} as attachment {added.Id} "
                    + $"({added.Size} bytes).",
                    ToolOutputs.Node(new AddedAttachmentOutput
                    {
                        Outcome = Outcomes.Ok,
                        Key = key,
                        AttachmentId = added.Id,
                        FileName = added.FileName,
                        Size = added.Size,
                    }));

                return new Written(rendered, $"attachment {added.Id} on {key}");
            },
            cancellationToken);
    }
}
