# Context

Domain glossary for jira-server-mcp — an MCP server that lets coding agents work against a
legacy, self-hosted Jira. The vocabulary here is deliberately narrower than Atlassian's, because
Atlassian's own terms collapse Cloud and Server concepts that behave nothing alike.

## Instance and identity

**Jira Server** — a self-hosted Jira, whether licensed as Server or as Data Center. Both expose
the same REST surface, and this project treats them as one target.
_Avoid_: on-prem Jira, Jira DC, plain "Jira" when the distinction from Cloud matters.

**Jira Cloud** — Atlassian's hosted product. A different API, a different auth model, and
explicitly out of scope. Named here only so the boundary stays visible.

**Profile** — a named Jira Server this installation can talk to: base URL, credential, and the
recorded capability probe. The unit of configuration, of credential isolation, and of blast
radius.
_Avoid_: instance, connection, account, environment.

**Connected profile** — the mapping from a profile and its personal access token to the
configuration a Jira client needs: base URL, token, certificate authority bundle path. The shape
that must not differ between callers, kept in one place rather than assembled by hand at each one.
_Avoid_: client options, connection settings.

**Personal access token** — the bearer credential a Jira user issues to themselves and can
revoke at any time. The only credential this project accepts.
_Avoid_: API token (that is Cloud's), API key, password.

**Credential store** — the operating system's secret store, which holds a profile's token.
Whether that is Keychain, Credential Manager, or Secret Service is an implementation detail; the
term covers all of them, and the encrypted-file fallback too.
_Avoid_: vault, keyring, secret manager.

## Capability and compatibility

**Capability probe** — the recorded answer to "what is this Jira and what does it have": version,
deployment type, and whether Jira Software is licensed. Taken once, stored on the profile,
refreshed on demand. Version-conditional behaviour reads the probe; it never re-asks Jira.
_Avoid_: version check, feature detection, handshake.

**Platform API** — the REST surface every supported Jira Server has: issues, projects, users,
metadata. Reached at `/rest/api/2`.

**Software API** — the REST surface that exists only where Jira Software is licensed: boards,
sprints, backlog. Reached at `/rest/agile/1.0`. Its absence is normal, not an error.
_Avoid_: Agile API when precision matters — the term names a licence, not a methodology.

## Tool surface

**Tool surface** — the value produced by pairing an operator's grant set with a profile's
capability probe: exactly the tools a server registers. Named once, as a table pairing each tool
with what it requires, rather than as control flow scattered through the serve verb.
_Avoid_: registration logic, the if-chain.

**Tool** — one MCP operation an agent can call. The unit an agent sees, chooses between, and
pays context for. Not a REST endpoint: a tool may combine several, and a REST endpoint may
surface as no tool at all.

**Tool call** — one invocation of a tool, and the module that owns what an agent sees when it
fails: a Jira that refused, a Jira that could not be reached, and a Jira that did not answer in
time. The per-tool advice is data handed to it; the sentences around that advice are not.
_Avoid_: handler, invocation, request.

**Grant** — a named category of write permission (`issues:write`, `comments:write`,
`worklogs:write`, `links:write`) that the operator hands to one MCP client. Without a grant, the corresponding
tools are not registered, so an agent cannot attempt them.
_Avoid_: scope, permission, role — all three already mean something inside Jira.

**Protocol seam** — the boundary at which an agent observes a tool: a real MCP client and server,
with Jira replaced by an HTTP double. Where tool-specific branching is proven. See ADR-0008.

**Expansion** — an optional extra section of an issue read: comments, transitions, changelog,
links, worklogs. Opt-in, because each one costs the agent context it may not need.
_Avoid_: Jira's own "expand", which names a different and overlapping mechanism.

**Bulk read** — one call resolving several issue keys at once. Each issue is rendered whole rather
than abridged for company, and each key succeeds or fails alone: one key that names nothing costs
the caller that key and no other. What distinguishes it from a search that happens to match those
keys is the failure model, not the number of issues.
_Avoid_: batch, multi-get, bulk operation — the last already names a write this project does not do.

**Issue link** — a typed, directional relation from one Jira issue to another. It is a field on
the issue, it is visible to JQL, and its type is named twice — once from each end.
_Avoid_: link used bare, which collapses this with the remote link below.

**Remote link** — a relation from an issue to a URL outside Jira: a pull request, a build, a
document. Untyped, not a field on the issue, and identified by a `globalId` this server derives
from the URL, so attaching the same URL twice updates one link rather than making two.
_Avoid_: web link, external link, attachment — the last is a file Jira stores itself.

**Relation phrase** — the direction-specific wording Jira publishes for a link type: "blocks" and
"is blocked by" for the one type `Blocks`. The unit this server's tools take, in place of a type
name paired with a direction, so that which end is which can never be got wrong. A phrase may be
symmetric, and two types may share one.
_Avoid_: inward, outward, link type name.

**Field projection** — the set of issue fields a response carries. Defaulted to a small
whitelist, widened explicitly by the caller. The main lever against a raw Jira issue's
hundred-kilobyte payload.
_Avoid_: filter, field selection.

**Response budget** — the limits on what a response is allowed to cost an agent: text per line,
prose, issue-read expansion entries, and default and largest page sizes. Every renderer and every
page reads these limits from one module; the cutting mechanics remain with the renderer that cuts,
and the paging mechanics belong to the page of issues below.

**Page of issues** — the answer six tools give: a JQL search, the change feed, the operator's
canned queries, a board's backlog, a sprint. One module states the whole recipe — the floor under
the start position, the clamp on the page size, the widened projection, the render, and the prefix
line a tool puts above it — and takes the fetch as a delegate, so a tool contributes the query or
the identifier and nothing else. A board or sprint _listing_ is not one: it pages rows that are not
issues, through a renderer of its own.
_Avoid_: search result, which names only the first of the six.

**Canned query** — a fixed JQL the server owns, exposed as a named tool, so an agent spends no
context authoring the query. The name is the contract; a parameter that changes what the query
means belongs in `jira_search` instead.

**Field alias** — a name an operator declares on a profile for one of that Jira's fields:
`story_points` for `customfield_10010`. An additional name, never a rename — a read shows both, and
a write accepts either — because the identifier is still what every part of Jira that has no alias
requires. Declared, never derived from Jira's own field names: derivation would give two instances
the same alias by accident, and the operator's intent is the one thing nothing else can supply.
_Avoid_: field mapping, custom field name, friendly name.

**Idempotency key** — a string a caller invents and hands to a write that Jira offers no natural
way to repeat safely: a create, a comment, a worklog. This server records the key before it sends
the write, so a second call carrying it writes nothing and is told what became of the first — above
all when the first timed out and nobody knows. The record lasts as long as the server process and
no longer, which is a bound the tools state rather than hide.
_Avoid_: request id, transaction id, deduplication token.

**Operator-defined query** — a canned query an operator declares on a profile rather than one this
repository ships: a name, a fixed JQL, and a description, registering as `jira_q_<name>`. The prefix
is what keeps an operator's name from colliding with a built-in tool. Subject to the same grant
gating, rendering and response budget as any other tool, and capped, because every registered tool
costs an agent context in every conversation.
_Avoid_: custom tool, user-defined query, saved filter — the last is Jira's own concept and a
different thing.

**Untrusted content** — free text authored inside Jira: descriptions, comments, custom field
values, project names, transition names, and the text Jira returns when it refuses a request —
a field validator's message is as admin-authored as a description is. It reaches a model, so it
can carry instructions aimed at that model. The term marks provenance, not suspicion of any
particular string, and provenance does not change because the text arrived on a failure.
_Avoid_: user input, which suggests the tool's caller wrote it.

**Workflow prompt** — an MCP prompt this server owns: a multi-step procedure a human picks in
their client, most often as a slash command, to hand a whole unit of work to an agent. Attended by
the protocol's own design, since nothing an agent does mid-loop can fetch one, and static text, so
it reads nothing and can go nowhere stale. See ADR-0011.
_Avoid_: prompt used bare, which in this project also means asking the operator for a token at the
terminal; command; workflow, which names the Jira concept a transition belongs to.

**Structured content** — the machine-shaped half of a tool result, carried beside the prose: issue
keys, status ids and names, transition ids, usernames, paging positions, per-key outcomes. Narrower
than MCP's own term, and narrower on purpose — it carries identifiers and the values Jira
enumerates, never issue prose, which stays inside the untrusted content region. A contract: fields
are added, never removed or retyped. See ADR-0009.
_Avoid_: structured output, JSON response, the machine-readable half.
