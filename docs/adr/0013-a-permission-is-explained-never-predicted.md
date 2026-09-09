# ADR-0013: A Jira permission is explained, never predicted

**Status:** Accepted (2026-08-31), amended (2026-09-03)

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
   with its own evidence to gather, and it is filed rather than smuggled in. *Answered by the
   2026-09-03 amendment below, which is where the reasoning lives.*
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


## Amendment (2026-09-03): a 401 that is a refusal, told apart by the lookup itself

Finding 3 above left the `401` arm alone, wanting evidence. #142 was filed to gather it and asked
two questions: whether any other endpoint answers `401` for a missing permission, and whether Jira's
response body reliably distinguishes a permission `401` from a credential one. **Neither turned out
to be load-bearing**, because the discriminator was already built by this ADR.

`PermissionAdvice.AskAsync` reads `/rest/api/2/mypermissions` **on the same personal access token as
the write**. A token Jira will not accept cannot read it either, so the lookup fails and answers
nothing. A live token belonging to an account that lacks `LINK_ISSUES` gets a `200` carrying
`LINK_ISSUES: false`. This ADR already measured that `mypermissions` answers on 8.20.7 for a
restricted account as well as for an administrator. So **an answer arriving at all is proof the
credential is live**, and the refusal is therefore not a credential problem — established without
parsing a body, reading a header, or matching an endpoint.

### The decision

`ToolCall`'s lookup gate widens from `Forbidden` to `Forbidden or Unauthorized`, still only where a
`claim` was passed, which is to say only for a write. That one condition is the centre of the
change. A read that answers `401` claims no permission, asks nothing, and keeps the sentence it has
always had.

Three alternatives were considered again and rejected again:

- **Branch on the response body**, on `errorMessages` being non-empty. This is the endpoint parsing
  this ADR argues against, wearing a different hat.
- **Branch on response headers** — `X-Seraph-LoginReason`, `X-Authentication-Denied-Reason`.
  `JiraApiException` carries the status, the endpoint, `errorMessages` and `fieldErrors` and
  deliberately nothing else. Widening a public type in `JiraServerMcp.Jira` to carry headers, for
  one message, buys a discriminator that already exists.
- **A `describeApiFailure` arm on `jira_link_issues` alone.** Cheapest, and it hard-codes "only the
  link answers `401`" — the very claim #142 said was unverified. The widened gate is correct whether
  the link is alone in it or not, which is precisely why the `401` census #142 asked for is no
  longer worth running.

### Four standings, not a flag

`PermissionAnswer.Held` becomes `PermissionAnswer.Standing`, one of four. `AskAsync` always returns
an answer now, because a claim has always been made by the time it is called, so a null
`PermissionAnswer` means only "a read, which claimed nothing".

| Standing | What happened | What a `401` may conclude |
|---|---|---|
| `Held` | Jira named the key and the account has it | the token is live; the permission is not the reason either |
| `Absent` | Jira named the key and the account lacks it | the token is live; this is the reason |
| `Unlisted` | Jira answered, and never named the key | the token is live; the claim is unresolved |
| `Unanswered` | Jira could not be asked | nothing, the token included |

`Unlisted` and `Unanswered` both mean "nothing is known about the key", and an earlier draft of this
change folded them into one null. That was wrong, and wrong in the direction this ADR exists to
prevent: `Unlisted` **is** an answer, so it proves the token is live, and collapsing it into
`Unanswered` sent a caller whose credential demonstrably works off to rotate it — #142's own defect
in a quieter voice. It is not hypothetical. The comment guarding the branch was written for an
instance at the 8.14 support floor whose enumeration may not carry a key, and
`JiraClient.GetMyPermissionsAsync` produces the same empty map from a `200` with no `permissions`
node at all.

Under a `403` all three of `Absent`'s siblings behave as before, because ruling the token out is
worth a sentence only where the token was under suspicion, and on a `403` it never was.

`PermissionAdvice.Sentence` returns null for the two standings that have nothing to say about the
key, and `JiraToolError.Refused` picks its opening from whether a sentence came back. A standing
with no sentence therefore cannot borrow another standing's — the property the nullable flag left to
a guard in a different file.

### The held branch's tail is keyed on the status

The `403` tail reads "Jira also answers 403 for an instance in read-only or maintenance mode and for
a throttled login". Interpolating the status number into that under a `401` would have **manufactured
a new false claim** — read-only mode and throttling are `403`'s causes, not `401`'s — which is this
ADR's own defect one status code along. So the tail is chosen by status, and the `401` tail says the
useful thing instead: the lookup that answered was made with this same token, so the token is
neither invalid nor revoked. Under a `401` it hangs off **both** held branches rather than only the
one with nothing else missing, because ruling the token out is the whole reason a `401` reaches the
refusal wording at all.

### What this costs

One extra round trip before a genuinely revoked token is reported on a write, since the lookup now
fires on the `401` it will also fail. This is the same trade this ADR already accepted for the
`403` — a round trip on a path where one has already failed — and it buys the difference between an
agent reading a permission key and an agent burning a credential rotation on a permission problem.
Reads, which are the overwhelming majority of calls, are unaffected.

### What was measured

- **A bearer token this Jira never minted cannot read `mypermissions`.** Automated as
  `A_token_jira_does_not_recognise_cannot_read_the_permission_lookup_either` in
  `JiraPermissionAdviceTests`, because it is the assumption the whole discriminator rests on and
  belongs in the suite rather than in a paragraph.
- The end-to-end case — a restricted account refused a link with `401` while `mypermissions` still
  answers `200` on the *from* issue — needs the second account and narrowed permission scheme this
  ADR records as hand-run, and is unchanged from the original measurement above.

### Still not changed

`AuthVerbs`. `auth status` and `auth login` verify against `/rest/api/2/myself`, a read claiming no
permission, so their `401` is unambiguous by construction.

The five writes whose missing permission arrives as a `400` — comment, worklog, edit, create,
transition — are filed as #148. That advice is thin rather than wrong, and it turns on a different
question: a `403` is rare enough to afford an unconditional round trip and a `400`, which is mostly
ordinary field validation, is not.
