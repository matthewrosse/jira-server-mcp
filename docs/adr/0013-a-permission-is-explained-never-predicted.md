# ADR-0013: A Jira permission is explained, never predicted

**Status:** Accepted (2026-08-31)

## Context

A **grant** answers "may this client write at all", locally, before a tool is registered. Nothing
answered "may this *account* write *here*", which is Jira's half of the same question. A `403` said
which operation and which endpoint and stopped there, leaving the agent to guess between a missing
permission and the several other things Jira answers `403` for.

The obvious shape was a `jira_my_permissions(projectKey)` preflight tool, and #129 was originally
written as one. It is rejected. Recording why is most of this ADR's job: without it, the next
person to read the issue list sees a permission endpoint nobody exposed and files it again.

**A green preflight can still fail.** `403` is not only a permission scheme.

- Data Center in read-only or maintenance mode returns `403` on every write.
- Authentication throttling returns `403` with `X-Authentication-Denied-Reason`.

`mypermissions` answers "you have `EDIT_ISSUES`" for both. (The spec also cited an attachment POST
missing its `X-Atlassian-Token: no-check` header as a third `403`. Measured on 8.20.7 it is a `404`
reading "XSRF check failed" — which is a worse answer, not a better one, and does not disturb the
argument.)

**The refusals an agent workflow actually meets never reach `403` at all.** A workflow condition
hides a transition, which fails as `400`. A screen refuses a field, which fails as `400` with field
errors. Issue-level security hides the issue, which fails as `404` — the README already says Jira
collapses "does not exist" and "you cannot see it".

A tool whose description would have to read "this cannot tell you whether the write will succeed"
should not be registered, and every registered tool is paid for in every conversation whether it is
called or not.

## Decision

`/rest/api/2/mypermissions` is read **after** a refusal and nowhere else. The answer is an
explanation, never a prediction, and there is no tool: nothing an agent can call early, and so no
misuse to design against. The lookup costs a round trip only on a path where one has already
failed.

The shape follows from that:

- **Writes only.** A read that lacks `BROWSE_PROJECTS` is answered `404`, never `403`, so a browse
  row would never fire.
- **Keyed by Jira's own permission key, written bare** — `TRANSITION_ISSUES`. Not the display name:
  an administrator can rename that, which makes it untrusted content and would drag a second
  envelope into a message that already carries one. The bare key is also what a human searches the
  permission-scheme screen for.
- **`ToolCall` orchestrates, through a lookup delegate the tool closes over.** `ToolCall` is the
  failure seam and owns this vocabulary, but it has never held a `JiraClient`, and giving it one
  would put an unused parameter at every read tool's call site. `JiraToolError` stays a pure
  formatter and receives the answer as a parameter.
- **The tool passes the key and the scope**, the way it already passes `whenUnreachable`. The
  endpoint is never parsed: the call site holds the fact, and six of the eight write endpoints
  carry an issue key in the path while create (`POST rest/api/2/issue`) and link
  (`POST rest/api/2/issueLink`) carry nothing. `JiraToolError.IsAnIssue`'s `createmeta` exclusion is
  a standing demonstration of what endpoint parsing costs.
- **`issueKey` where one exists, `projectKey` only for create.** A project-level evaluation cannot
  honour a scheme that grants Edit Issues to the current assignee or reporter; an issue-scoped one
  can. A link passes the *from* issue's key — one scope, because a refusal names one endpoint.
- **One claimed key per tool, plus the others the same answer already reveals.** Jira Server has no
  `permissions=` filter and returns the whole enumeration in one response, so where the claimed key
  is held the sentence can name any other write permission the account lacks in that scope — which
  is what turns a create refused for `ASSIGN_ISSUES` from a dead end into an answer.
- **A failed lookup is swallowed silently**, on the caller's cancellation token, falling back to
  today's exact sentence. `mypermissions` may not exist on the 8.14 support floor, may time out, and
  may itself be refused; a diagnostic that reports its own failure teaches nothing about the write
  and reads like a third failure, and a separate timeout budget for a diagnostic is policy nobody
  asked for.
- **Nothing is cached.** Permissions vary per project and per issue, so this is not a capability
  probe and nothing about it is recorded on the profile.
- **Message order**: what Jira refused, then why, then the caller's state clause, then the status
  line and Jira's own words. Cause before consequence, and all of this server's prose before
  anything Jira authored.
- **Structured content gains `missingPermission` on the absent branch only** (ADR-0009). Rule 3
  promises structure on every result, not a field for every sentence, and a field is added and never
  removed — so the narrow field keeps the wider one available, while the wider one could not be
  taken back.

## What was measured, on a real 8.20.7

The operation table was written against a genuine Jira Server 8.20.7 rather than against a double:
a second account was created, a permission scheme granting it nothing but Browse Projects was
attached to the seeded project, and each of the eight writes was sent as that account. The result
changes what this feature can be claimed to do, so it is recorded rather than summarised.

| Write | Endpoint | Answer with the permission missing |
|---|---|---|
| Comment | `POST issue/{key}/comment` | **400**, "you do not have the permission to comment on this issue" |
| Worklog | `POST issue/{key}/worklog` | **400**, "…to associate a worklog to this issue" |
| Edit | `PUT issue/{key}` | **400**, field error: "cannot be set. It is not on the appropriate screen" |
| Create | `POST issue` | **400**, the same field error |
| Transition | `POST issue/{key}/transitions` | no transitions are published at all, so the call is **400** |
| Issue link | `POST issueLink` | **401**, "No Link Issue Permission for issue 'X'" |
| Remote link | `POST issue/{key}/remotelink` | **403**, `LINK_ISSUES` |
| Attachment | `POST issue/{key}/attachments` | **403**, `CREATE_ATTACHMENTS` |

Four things follow, and each was an assumption before it was a measurement.

1. **Only two of the eight writes reach a `403` for a missing permission.** The lookup still hangs
   off every write, because a `403` that arrives on any of the other six is then one of the causes
   that is *not* a permission — read-only mode, throttling, a header — and "the account does have
   `ADD_COMMENTS` here" is precisely the answer that case needs. The held branch is the common one,
   not the exceptional one.
2. **A missing `ASSIGN_ISSUES` never surfaces as a `403` from anything this server sends.** A create
   or an edit carrying an assignee is refused with a `400` field error on `assignee`; only Jira's
   dedicated assignee endpoint, which this server does not use, answers `403`. `ASSIGNABLE_USER` is
   named by neither. `ASSIGN_ISSUES` stays in the table as a key that is *reported* beside another
   and never claimed — which makes the held branch the only way an agent learns of it.
3. **A refused issue link answers `401`, not `403`.** This server reads `401` as an invalid or
   revoked token and tells the caller to run `auth login`, which is the wrong advice here. Left
   alone deliberately: rewording the `401` arm on the strength of one endpoint is a separate change
   with its own evidence to gather, and it is filed rather than smuggled in.
4. **An attachment POST without `X-Atlassian-Token: no-check` answers `404` ("XSRF check failed"),
   not `403`.** The rejected-preflight argument above cited it as a `403`; it is still an argument
   against a preflight, and the specific claim was wrong.

`mypermissions` itself answers on 8.20.7 for a restricted account as well as for an administrator,
returns 73 keys, and ignores `permissions=` — a filtered request comes back byte-identical to an
unfiltered one. The 8.14 support floor is untested; that decides only how often the silent fallback
fires, not whether it is needed.

## Consequences

- The refusal's opening sentence has two forms. Where nothing was asked, or the lookup failed, it is
  the sentence this server has always used. Where Jira answered, the opening stops asserting a
  missing permission, because the next line says whether there is one — and on the branch where the
  account holds what it claimed, the old opening would contradict it.
- This does **not** fix the partial state a half-finished workflow leaves behind. A transition that
  succeeded before a refused comment stays done, and `ImplementIssuePrompt` is unchanged. It makes
  the failure legible and nothing more.
- Rejected: `jira_my_permissions` as a tool, for the reasons above. If it is proposed again, the
  question to answer first is what its description could honestly claim.
- Rejected: caching the answer on the profile. A capability is a property of the instance; a
  permission is a property of the account, the project and often the issue, and a cached one would
  be wrong in exactly the cases that matter.
