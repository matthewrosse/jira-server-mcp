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

**Resolved profile** — the profile and the token this run will use, or the reason there are none.
Every CLI verb needing a token reaches it through the same sequence and the same missing-token
sentence, with the caller supplying only the clause naming what it therefore did not do. See
**Connected profile** for what this becomes once a client is built from it.
_Avoid_: authenticated profile, the triple.

**Connected profile** — a profile and its personal access token taken together as a working Jira
client: the client is built, used and disposed in one place rather than assembled by hand at each
caller. The term carries the vocabulary for what happened when using one fails — Jira refused,
Jira could not be reached, Jira did not answer in time, or this tool itself faulted.
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
`worklogs:write`, `links:write`, `attachments:write`) that the operator hands to one MCP client.
Without a grant, the corresponding tools are not registered, so an agent cannot attempt them.
_Avoid_: scope, permission, role — all three already mean something inside Jira. What
**Jira permission** names is that other thing.

**Jira permission** — a named right in a project's permission scheme, spelled as Jira's own bare
key: `EDIT_ISSUES`, `TRANSITION_ISSUES`. A **grant** decides whether this client may attempt a
write; this decides whether the account the server authenticates as may perform one. Asked about
only after Jira has already refused a write, never cached, and never a promise about the next
write — a `403` also comes from a missing cross-site request forgery header, from an instance in
read-only mode, and from a throttled login. See ADR-0013.
_Avoid_: grant, right, access level.

**Protocol seam** — the boundary at which an agent observes a tool: a real MCP client and server,
with Jira replaced by an HTTP double. Where tool-specific branching is proven. See ADR-0008. The
staging it takes — the double, a registered profile, a stored token, a client over stdio — lives in
the `ProtocolSeam` fixture, and a test in that project fails if a file stages it by hand.

**Verb seam** — the boundary at which an operator observes a verb: the exit code, and what is
printed. Proven over the CLI as a real process against an HTTP double, because the failure ladder
writes to standard error and returns nothing a caller could inspect. See ADR-0008, clause 4. What
varies here is the ladder itself — unregistered, registered, logged in — so a verb that is the
subject of a test is spelled out, and a verb that is only staging is not. The staging it
takes — the double, a registered profile, a stored token — lives in the `VerbSeam` fixture, and a
test in that project fails if a file stages it by hand. The account payload it answers with lives
in `tests/Shared`, guarded across both seams' projects at once, because the shape of Jira's user
resource is one fact and the fields nothing asserts on are the ones that drift.
_Avoid_: command, CLI test — the first already names the System.CommandLine symbol.

**Expansion** — an optional extra section of an issue read: comments, transitions, changelog,
links, worklogs, attachments, sub-tasks. Opt-in, because each one costs the agent context it may
not need. An expansion reaches its section by exactly one of three mechanisms — a collection
field, Jira's own expand parameter, or a request of its own — and which one it is belongs to the
expansion, not to the caller that asks for it.
_Avoid_: Jira's own "expand", which names a different and overlapping mechanism.

**Section** — a block of an issue read that is rendered on its own terms rather than as a line of
the field projection: the comments, the history, the links. Every section is an expansion's
answer, so a section nobody asked for is not rendered at all — and one that was asked for renders
empty rather than being left out, which is the difference between "there are none" and "you did
not ask".
_Avoid_: block, panel, part.

**Collection field** — a projected field Jira answers with as an array rather than a value:
`comment`, `issuelinks`, `worklog`, `attachment`, `subtasks`. It is asked for through the field
projection but is not part of it, so it is lifted out and read into a section; left in, it renders
as a JSON blob.
Which names these are is decided where the expansions are named, not where the response is read.
_Avoid_: array field, multi-value field, sub-resource.

**Bulk read** — one call resolving several issue keys at once. Each issue is rendered whole rather
than abridged for company, and each key succeeds or fails alone: one key that names nothing costs
the caller that key and no other. What distinguishes it from a search that happens to match those
keys is the failure model, not the number of issues.
_Avoid_: batch, multi-get, bulk operation — the last already names a write this project does not do.

**Sub-task** — an issue Jira holds beneath another issue, in the one relation Jira models as a
hierarchy rather than as a link. Reached as an expansion of the parent, where it renders with the
status that says whether the work is done; the other end of the same relation is the `parent`
field, which is in the default projection and renders as the parent's key and status both.
_Avoid_: child issue, sub-issue, task — the last is an issue type Jira already ships.

**Issue link** — a typed, directional relation from one Jira issue to another. It is a field on
the issue, it is visible to JQL, and its type is named twice — once from each end.
_Avoid_: link used bare, which collapses this with the remote link below.

**Remote link** — a relation from an issue to a URL outside Jira: a pull request, a build, a
document. Untyped, not a field on the issue, and identified by a `globalId` this server derives
from the URL, so attaching the same URL twice updates one link rather than making two.
_Avoid_: web link, external link, attachment — the last is a file Jira stores itself.

**Attachment** — a file Jira stores on an issue. Reached as an expansion that names and sizes it,
then fetched one window at a time, because a log or a pasted CSV is routinely larger than anything
worth reading in one go. The media type Jira claims for it is advisory and nothing branches on it;
whether the bytes are text is decided by reading them. It crosses this server's boundary as
**content**, never as a path — nothing here opens a file on the machine the server runs on, in
either direction, so a filename is a label a human will later download rather than a location.
_Avoid_: file, document, remote link — the last is a URL Jira holds, not bytes this server moves.

**Published vocabulary** — the words one Jira publishes for a kind of choice: the transitions
available on this issue, the relation phrases this instance publishes. A word the caller used must
resolve to exactly one of them, or be refused with the alternatives named — a word that names none
and a word that names two are both refusals, because picking one of two would write something
nobody asked for. Not every named thing Jira holds is one: an issue type, a component, a version
or a resolution is sent as the caller wrote it and Jira refuses it.
_Avoid_: enum, allowed values, lookup table.

**Query catalogue** — what one Jira will accept in a JQL query: which fields are queryable, the
name each is queryable under, the operators each takes, and the functions this instance publishes.
Published and never matched — nothing resolves a caller's word against it, which is what
distinguishes it from a **published vocabulary** above. The read-side counterpart of a **screen**:
a screen says what a write will accept, a query catalogue says what a query will.
_Avoid_: JQL vocabulary, autocomplete data, field catalogue.

**JQL name** — the name a field is queryable under, which for a custom field is neither its
identifier nor, in general, its bare display name: `customfield_10107` is queryable as `cf[10107]`
or as `"Story Points"` — quotes included, because Jira publishes the quoted form — and never under
the identifier every other part of this server uses. The gap is why a **field alias** cannot simply
be substituted into a query.
_Avoid_: clause name (Jira's own word for the same thing, and one nothing else here uses), field
name.

**Relation phrase** — the direction-specific wording Jira publishes for a link type: "blocks" and
"is blocked by" for the one type `Blocks`. The unit this server's tools take, in place of a type
name paired with a direction, so that which end is which can never be got wrong. A phrase may be
symmetric, and two types may share one.
_Avoid_: inward, outward, link type name.

**Field projection** — the set of issue fields a response carries. Defaulted to a small
whitelist, widened explicitly by the caller. The main lever against a raw Jira issue's
hundred-kilobyte payload.
_Avoid_: filter, field selection.

**Screen** — the set of fields one Jira publishes for one operation on one issue type: the create
screen, the edit screen. Keyed on issue type and operation, never on status — a screen scheme maps
screens to operations, and a stock scheme maps all of them to one screen, so two screens that
differ are an administrator's doing. A screen names each field's identifier, whether it is
required, its allowed values where Jira enumerates them, and which **operations** it accepts — a
field can be on the screen and still not be settable, and one that is settable may not be settable
the way the caller meant.
_Avoid_: form, field configuration (Jira's own, and a different thing), metadata.

**Operation** — what one Jira permits doing to one field on a **screen**: `set`, `add`, `remove`.
Published per field and per screen, so it is a fact about this field on this issue rather than
about the field's type: `labels` publishes all three, `issuelinks` publishes `add` and no `set` at
all, and a field publishing none is on the screen and writable by nothing. Distinct from the tool
verb carrying it — one call to `jira_update_issue` sets some fields and adds to others — and
distinct from this project's own prose sense of "operation" as a unit of work, as in "reassignment
should not cost two operations".
_Avoid_: action, verb, mutation.

**Response budget** — the limits on what a response is allowed to cost an agent: text per line,
prose, issue-read expansion entries, and default and largest page sizes. Every renderer and every
page reads these limits from one module; the cutting mechanics remain with the renderer that cuts,
and the paging mechanics belong to the page of issues below. It bounds **answers** only: a cap on
what a caller may send is a different thing, is spent before this server ever sees it, and lives
with the tool that takes it.

**Page of issues** — the answer six tools give: a JQL search, this account's open issues, the
change feed, the operator's canned queries, a board's backlog, a sprint. One module states the whole recipe — the floor under
the start position, the clamp on the page size, the widened projection, the render, and the prefix
line a tool puts above it — and takes the fetch as a delegate, so a tool contributes the query or
the identifier and nothing else. A board or sprint _listing_ is not one: it pages rows that are not
issues, through a renderer of its own.
_Avoid_: search result, which names only the first of the six.

**Canned query** — a fixed JQL the server owns, exposed as a named tool, so an agent spends no
context authoring the query. The name is the contract; a parameter that changes what the query
means belongs in `jira_search` instead. The rule binds where the name is the whole contract — where
an agent chooses before reading a schema — and not to a tool whose parameters it reads first.

**Assignable user** — a user one Jira will accept as the assignee of a given issue, or of
anything in a given project. Always a subset of the users a directory search matches, and never
derivable from a user's own record, because the permission lives on the project rather than on the
person. The gap is invisible until a write is refused, and the refusal names a field rather than a
permission — so it is asked about up front, by anchoring the user search to an issue or a project.
_Avoid_: eligible user, project member, team.

**Field alias** — a name an operator declares on a profile for one of that Jira's fields:
`story_points` for `customfield_10010`. An additional name, never a rename — a read shows both, and
a write accepts either — because the identifier is still what every part of Jira that has no alias
requires. Declared, never derived from Jira's own field names: derivation would give two instances
the same alias by accident, and the operator's intent is the one thing nothing else can supply.
_Avoid_: field mapping, custom field name, friendly name.

**Remaining estimate** — the time Jira believes is still needed on an issue, as against the
original estimate it was first given. Logging work is the only thing here that moves it: a worklog
reduces it by the time logged unless the caller asks for it to be left. The number is never
reported back by a write, because Jira's answer to a worklog carries the worklog and nothing about
the issue's time tracking; it is read through the `timetracking` field.
_Avoid_: estimate, time estimate, original estimate — the last is a different number this server
never writes.

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

**Saved filter** — a named JQL query a human wrote and kept inside Jira, owned by whoever wrote
it. Not this server's object and not an operator-defined query: what separates them is cost. An
operator-defined query is this deployment's own configuration and **registers as a tool**, so it is
paid for in every conversation; a saved filter is Jira's own, is **listed and never registered**,
and is paid for in the one call that lists it. It is run by naming `filter = <id>` in a search,
which is ordinary JQL — so this server discovers filters and never executes one. Only the
favourites of the account the token belongs to are reachable: Jira Server publishes no endpoint for
every filter an account can see.
_Avoid_: canned query, operator-defined query, and the bare word filter — this glossary spends
that one on field projection, so the two words travel together everywhere but a code identifier.

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
