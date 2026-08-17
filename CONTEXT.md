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
`worklogs:write`) that the operator hands to one MCP client. Without a grant, the corresponding
tools are not registered, so an agent cannot attempt them.
_Avoid_: scope, permission, role — all three already mean something inside Jira.

**Expansion** — an optional extra section of an issue read: comments, transitions, changelog,
links, worklogs. Opt-in, because each one costs the agent context it may not need.
_Avoid_: Jira's own "expand", which names a different and overlapping mechanism.

**Field projection** — the set of issue fields a response carries. Defaulted to a small
whitelist, widened explicitly by the caller. The main lever against a raw Jira issue's
hundred-kilobyte payload.
_Avoid_: filter, field selection.

**Response budget** — the limits on what a response is allowed to cost an agent: text per line,
prose, issue-read expansion entries, and default and largest page sizes. Rendering and paging
read these limits from one module; their cutting and paging mechanics remain where they belong.

**Untrusted content** — free text authored inside Jira: descriptions, comments, custom field
values, project names. It reaches a model, so it can carry instructions aimed at that model. The
term marks provenance, not suspicion of any particular string.
_Avoid_: user input, which suggests the tool's caller wrote it.
