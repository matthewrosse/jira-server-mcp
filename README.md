# jira-server-mcp

An MCP server that gives a coding agent read and gated write access to a legacy, self-hosted
**Jira Server** — Server or Data Center, 8.14 and later. **Not Jira Cloud:** Cloud is a different
REST API with a different authentication model, and it is out of scope here. If your Jira lives at
`something.atlassian.net`, this is the wrong tool. If it lives on a host your organisation runs,
this is the right one.

It speaks the Model Context Protocol over stdio, authenticates with a **personal access token**
kept in your operating system's **credential store**, serves exactly one **profile** — one named
Jira Server — per process, and registers a write tool only where you handed that client the
matching **grant**. Seventeen tools, sized for an agent's context rather than mapped one-to-one onto
REST endpoints.

## Supported Jira versions

| | Versions |
|---|---|
| **Tested** | Jira Software **8.20.7** — the version the integration suite provisions and runs against, nightly, in CI |
| **Expected, unverified** | Jira Core and Jira Software 8.14–8.22, and Jira 9.x and 10.x Data Center |
| **Not supported** | Jira below 8.14 (no personal access tokens), and Jira Cloud (different API) |

Everything in the second row uses the same `/rest/api/2` surface and is expected to work, but
nobody has run it. It is listed as expectation, not as a claim. If you run this against another
version, an issue saying what worked and what did not is welcome.

## Requirements

- A Jira Server 8.14+ reachable over HTTPS. Plain `http://` is accepted only for a loopback
  address — `http://localhost`, `http://127.0.0.1`.
- A Jira account that can create a personal access token, and the Jira permissions you want the
  agent to have — this server has exactly the permissions of the token's user and no more.
- **For the .NET tool:** the .NET 10 SDK, which is what provides `dotnet tool`.
- **For the self-contained binaries:** nothing. They carry their own runtime.
- An MCP client that launches stdio servers: Claude Code, VS Code with Copilot, or any other.

## Installation

### The .NET tool (primary)

Releases are published to **GitHub Packages**, not to nuget.org — the public gallery waits until
the tool surface stops moving. GitHub Packages authenticates every read, so the feed needs a
GitHub personal access token with the `read:packages` scope, even though this repository is public:

```
dotnet nuget add source https://nuget.pkg.github.com/matthewrosse/index.json \
  --name jira-server-mcp \
  --username <your-github-username> \
  --password <a-github-token-with-read:packages> \
  --store-password-in-clear-text

dotnet tool install --global jira-server-mcp
```

The install needs no source flag once the feed is configured — the package id exists on no other
feed — and `--source https://nuget.pkg.github.com/matthewrosse/index.json` restricts it to that feed
if you would rather be explicit. `--source` takes the URL, not the name you gave the source. Upgrade
with `dotnet tool update --global jira-server-mcp`, and check what you have with
`jira-server-mcp --version`.

**Until the first release is tagged this feed is empty.** Build from source in the meantime; see
[Development](#development).

### The self-contained binaries (secondary)

Each release attaches one single-file binary per platform — `win-x64`, `win-arm64`, `osx-arm64`,
`osx-x64`, `linux-x64`, `linux-arm64` — with a `.sha256` beside it, for machines with no .NET
installed at all:

```
curl -fLO https://github.com/matthewrosse/jira-server-mcp/releases/download/v<version>/jira-server-mcp-<version>-<platform>
curl -fLO https://github.com/matthewrosse/jira-server-mcp/releases/download/v<version>/jira-server-mcp-<version>-<platform>.sha256
shasum -a 256 -c jira-server-mcp-<version>-<platform>.sha256
chmod +x jira-server-mcp-<version>-<platform>
```

The two Windows assets carry a `.exe` suffix — `jira-server-mcp-<version>-win-x64.exe`, and
`.exe.sha256` for its checksum. The other four have no suffix.

An MCP client then names the binary by absolute path instead of by command name. On macOS,
Gatekeeper quarantines a downloaded binary; clear it with
`xattr -d com.apple.quarantine ./jira-server-mcp-<version>-osx-arm64`.

There is deliberately no Docker image for the server (ADR-0004): a container cannot reach the host
credential store, and the credential store is the whole point.

## Quick start

Three commands and a configuration snippet. No Jira administrator is involved at any point.

**1. Register the Jira Server as a profile.** The URL is validated here — HTTPS, or a loopback
address — and it includes any context path your instance is served under. Nothing is called over
the network until you authenticate:

```
jira-server-mcp profile add work --url https://jira.example.com
```

**2. Authenticate.** Create a personal access token in Jira first — see
[Creating a personal access token](#creating-a-personal-access-token-in-jira-8) — then hand it
over. The prompt does not echo, and the token is validated against Jira before it is stored:

```
jira-server-mcp auth login work
```

```
Personal access token for profile 'work':
Signed in to https://jira.example.com/ as Jane Bloggs (jbloggs).
The personal access token for profile 'work' is stored in the macOS keychain, under the service 'jira-server-mcp'.
Jira 8.20.7 (Server), with Jira Software: the board, sprint and backlog tools are registered.
```

That last line is the capability probe, taken once and cached on the profile. It is what decides
whether the board, sprint and backlog tools exist at all.

**3. Point your agent at it.** For Claude Code, one command:

```
claude mcp add --transport stdio jira -- jira-server-mcp serve --profile work
```

That registers a read-only server. Add `--allow comments:write` — after the `--`, since everything
there belongs to the server — to let the agent comment. The
[MCP client configuration](#mcp-client-configuration) section has the file equivalent and the
VS Code form.

**4. Ask it something.** With the server connected, a question that used to need a browser:

```
> Which of my open issues in PROJ changed this week, and what did the last comment on each say?
```

The agent calls `jira_whoami`, then `jira_search` with the JQL it derives, then `jira_get_issues`
with `include: ["comments"]` for the ones that matter, in one call rather than one per issue.

## Creating a personal access token in Jira 8

Personal access tokens arrived in Jira Core and Jira Software 8.14, on Server as well as Data
Center. You issue one to yourself, and you can revoke it from the same screen:

1. In Jira, click your **avatar**, top right, and choose **Profile**.
2. Open the **Personal Access Tokens** tab — the direct URL is
   `<your-jira>/secure/ViewProfile.jspa?selectedTab=com.atlassian.pats.pats-plugin:jira-user-personal-access-tokens`.
3. Click the create button on that tab, give the token a name such as `jira-server-mcp`, and choose
   whether it expires. Your administrator may cap the maximum lifetime or forbid non-expiring
   tokens.
4. Copy the token. Jira shows it exactly once.
5. Paste it into `jira-server-mcp auth login <profile>`.

There is no refresh and no unattended re-authentication: when a token expires or is revoked, tool
calls fail with a message naming the profile, and a human creates a new token. That is a property
of the credential, not a limitation of this server.

**The token is never taken as an argument.** `--token` does not exist, and passing it is an error
rather than a warning: arguments are readable by every process on the machine and land in shell
history. A process that cannot be prompted — a container, a CI job — reads
`JIRA_SERVER_MCP__<PROFILE>__TOKEN` from its environment instead, where `<PROFILE>` is the profile
name upper-cased with anything that is not a letter or a digit replaced by an underscore.

## MCP client configuration

Both configurations below were run against a real Jira 8.20.7 rather than written from memory. The
grants in them are examples: leave `--allow` out entirely for a read-only server.

### Claude Code

`claude mcp add` writes the configuration for you:

```
claude mcp add --transport stdio jira -- jira-server-mcp serve --profile work --allow comments:write
```

`claude mcp list` then reports `✔ Connected`. To commit the configuration to a repository instead,
put it in `.mcp.json` at the project root:

```json
{
  "mcpServers": {
    "jira": {
      "type": "stdio",
      "command": "jira-server-mcp",
      "args": ["serve", "--profile", "work", "--allow", "comments:write"]
    }
  }
}
```

### VS Code and Copilot

`.vscode/mcp.json` in the workspace — note that VS Code's top-level key is `servers`, not
`mcpServers`:

```json
{
  "servers": {
    "jira": {
      "type": "stdio",
      "command": "jira-server-mcp",
      "args": ["serve", "--profile", "work", "--allow", "comments:write"]
    }
  }
}
```

VS Code needs to start the server before it can list the tools: **MCP: List Servers**, pick
`jira`, **Start Server**. `code --add-mcp '{"name":"jira","command":"jira-server-mcp","args":["serve","--profile","work"]}'`
adds it to your user profile instead of to the workspace.

### Anything else

Any client that launches a stdio server needs the same three things — a command, its arguments,
and no environment at all:

```json
{
  "command": "jira-server-mcp",
  "args": ["serve", "--profile", "work", "--allow", "issues:write,comments:write"]
}
```

With a self-contained binary, `command` is its absolute path. Grants may be repeated
(`--allow issues:write --allow comments:write`) or comma-separated; an unrecognised grant name
stops the server at startup rather than quietly registering nothing.

## Working with several Jira Servers

Profiles are named, and a process serves exactly one of them (ADR-0005). Nothing an agent sends
can name a Jira, because no tool takes an instance argument at all — which is what makes a write
landing on the wrong instance impossible rather than unlikely.

```
jira-server-mcp profile add work --url https://jira.example.com
jira-server-mcp profile add sandbox --url https://jira-sandbox.example.com
jira-server-mcp auth login sandbox
jira-server-mcp profile list
```

Two Jiras mean two entries in the client configuration:

```json
{
  "mcpServers": {
    "jira": {
      "type": "stdio",
      "command": "jira-server-mcp",
      "args": ["serve", "--profile", "work"]
    },
    "jira-sandbox": {
      "type": "stdio",
      "command": "jira-server-mcp",
      "args": ["serve", "--profile", "sandbox", "--allow", "issues:write,comments:write"]
    }
  }
}
```

The same pattern gives one profile different powers in different clients: read-only in the editor
that browses, write-enabled in the one that files bugs. The rest of the verbs:

```
jira-server-mcp profile list                # names and URLs — never a secret
jira-server-mcp profile refresh <name>      # take the capability probe again
jira-server-mcp profile remove <name>       # also deletes the stored credential
jira-server-mcp auth status <name>          # validate, and print the Jira user it resolves to
jira-server-mcp auth logout <name>          # delete the credential, keep the profile
```

Profiles live in `profiles.json` under `$XDG_CONFIG_HOME/jira-server-mcp`, and where that variable
is unset, under `~/.config/jira-server-mcp` on macOS and Linux and `%APPDATA%\jira-server-mcp` on
Windows. It holds a base URL, an optional certificate authority bundle path, and the cached
capability probe for each profile — and never a secret.

## The tools

Read tools are always registered. The four Jira Software tools are registered only where the
capability probe recorded a Jira Software licence. A write tool is registered only under its
grant — without it the tool does not exist, so an agent cannot discover it, attempt it, or spend
context learning that it is forbidden.

| Tool | Grant | What it does |
|---|---|---|
| `jira_whoami` | — | The Jira account this server is authenticated as. The first thing to check when something is forbidden. |
| `jira_search` | — | JQL search. 25 results by default, 100 at most, projected fields, and the total. |
| `jira_my_open_issues` | — | The caller's own unresolved issues, most recently updated first — the start-of-session work queue, with no JQL to author. |
| `jira_changed_since` | — | Issues that changed at or after a moment, oldest change first, with the watermark for the next call — the feed a scheduled workflow wakes on. |
| `jira_get_issues` | — | Up to 20 issues in one call, with `include` opting into `comments`, `transitions`, `changelog`, `links`, `worklogs` and `attachments` for each. Each key succeeds or fails on its own, and the transition list arrives with the issue the agent is about to transition. |
| `jira_get_attachment` | — | One attachment read as text, a window at a time, with the position to resume from. Whether a file is readable is decided by its bytes, not by the media type Jira claims. A file that is not text is listed and described — never inlined, and never read. |
| `jira_list_projects` | — | Key, name, id and type for every project the account can see. |
| `jira_get_project` | — | One project with its issue types, statuses, components and versions — everything a create needs, in one response. |
| `jira_get_create_fields` | — | What Jira will accept for a create: every field with its identifier, its type, whether it is required, and its allowed values. Read this before creating, or a required custom field rejects the create by identifier alone. |
| `jira_search_users` | — | Users by part of a name. Jira Server identifies a user by username, not by Cloud's account identifier. |
| `jira_list_boards` | — | Boards. **Jira Software only.** |
| `jira_list_sprints` | — | A board's sprints. **Jira Software only.** |
| `jira_get_sprint_issues` | — | The issues in one sprint. **Jira Software only.** |
| `jira_get_backlog` | — | A board's backlog: the issues no sprint has taken. **Jira Software only.** |
| `jira_create_issue` | `issues:write` | Create one issue. Custom fields go in the field map in Jira's own shape. |
| `jira_update_issue` | `issues:write` | Update fields, including the assignee. There is no separate assign tool. |
| `jira_transition_issue` | `issues:write` | Transition by transition *name*; the identifier is resolved here. Takes an optional comment and any screen fields the transition demands. |
| `jira_add_comment` | `comments:write` | Add one comment, in Jira wiki markup, stored as written. |
| `jira_add_worklog` | `worklogs:write` | Log work, with the time spent in Jira's own duration syntax (`"3h 30m"`), so how long a working day is stays Jira's decision. |
| `jira_link_issues` | `links:write` | Link two issues by the relation *phrase* Jira publishes — `"blocks"`, `"is blocked by"` — so the direction reads as English and cannot be got backwards. Takes an optional comment. |
| `jira_add_remote_link` | `links:write` | Attach a URL to an issue — a pull request, a build — so it lands in Jira's link panel rather than in a comment. The URL is the link's identity, so attaching it twice updates one link and says so. |

Deliberately absent: issue deletion, comment editing and deletion, attachments, unlinking,
sprint mutation, watchers, and votes. See [Known limitations](#known-limitations).

## Structured content

Every tool answers twice: the prose above, and a machine-shaped half in the result's
`structuredContent`, with the shape declared in the tool's `outputSchema`. The prose is for a model
to read; the structure is for a workflow to branch on, so that "is this issue still open?" or
"which of these twenty keys failed?" is a field to look at rather than English to re-parse. The
decision and its reasoning are [ADR-0009](docs/adr/0009-structured-content-beside-the-prose.md).

What it promises:

- **It is a contract.** A field may be added. None will be removed, and none will change type. The
  prose keeps its freedom to be reworded — nothing is promised to depend on it, which is the whole
  reason the structured half exists.
- **It is there on every result**, success and failure alike. Every one carries an `outcome`:
  `ok`, `jira_api` (with `statusCode`), `unreachable`, `timed_out`, or `refused` for a call this
  server rejected before reaching Jira. A bulk read keeps one shape whether or not `isError` is
  set, with its per-key failures in `failures`.
- **It carries identifiers and the values Jira enumerates.** Issue keys and ids, status ids and
  names, issue type names, transition ids, usernames, paging positions, counts. **Never issue
  prose** — no summaries, descriptions, comment bodies, or display names. Those live in the
  delimited region of the text half and nowhere else.
- **It is not a mirror of the text.** Where the response budget cuts a page, both halves are cut
  together and the structure carries the position to resume from, so the two can never disagree on
  what they contain.

Two things to know before depending on it. **The structured half is untrusted content too**: a
status name or an issue type name is admin-authored, and it is declared untrusted here, once,
rather than marked field by field — the line is drawn at prose, not at provenance. And **no tool
echoes its structured content as JSON in a text block**, which the MCP specification suggests for
clients that ignore `structuredContent`: honouring it would make you pay for every fact twice
against a server whose whole premise is what a response costs. A client that ignores the structure
still gets a complete, readable answer as prose.

`jira_get_create_fields` is the fullest of these: it carries every field a create must send, with
its identifier, its name, whether it is required, and its allowed values. **A field's `type` is
Jira's own `schema.type`** — `string`, `option`, `array` — passed through unchanged rather than
normalised into a vocabulary this server owns. A mapping would have to be maintained across every
Jira Server version, and a mistranslation is worse than an unfamiliar string: you can match an
unfamiliar string against the prose, but you cannot detect a wrong one. `hasAllowedValues` sits
beside `allowedValues` so that "constrained, but the list was cut" stays distinguishable from
"unconstrained", and `allowedValuesTruncated` says which happened.

`jira_list_projects` carries `cutByCap` rather than a resume position. Jira's project endpoint has
no page of its own — it answers with every project at once, and this server caps what it renders —
so a cut listing is narrowed with `jira_search`, or the project is named directly by its key to
`jira_get_project`. There is no next page to ask for, so no field pretends there is one.

`jira_list_boards` and `jira_list_sprints` carry **no `total`, and not a null one**. Jira's software
API says only whether a page is the last, never how many rows exist, and a paging field is present
here only where the server was actually given the number — absence means unknown, where zero would
mean none. `nextStartAt` is absent on a last page and otherwise advances past the page rather than
by the rows returned, because that API filters a page by permission after paging it.

`jira_search_users` and `jira_whoami` carry the **username and nothing personal**. On Jira Server
the username is what a write must send — there is no account identifier here, and anything shaped
like one belongs to Cloud — so it is both the identifier and the value that goes into an assignee
field. Display names and email addresses stay in the delimited region of the text half: the first
is how a person tells two similar colleagues apart, and the second is personal data this server
does not promise to carry. The user search reports no `total`, because Jira's does not.

`jira_link_issues` carries **both the relation phrase and the type name Jira stored it under**.
They are not the same string — `"is blocked by"` is stored under `Blocks` — and each answers a
question the other cannot: the phrase is what reads as English and what a repeat call would send,
the type name is what the issue panel and Jira's own payloads say. The two keys come back as you
named them, unswapped, because the phrase is what decided the direction. A phrase this Jira does
not publish, or one it publishes on two types, is a plain `refused` — the same answer
`jira_transition_issue` gives an unmatched or ambiguous transition name, which is the same problem
wearing a different hat.

`jira_add_remote_link` carries **`created`**: `true` where this call made the link, `false` where
it updated the one that was already there. That is the whole value of keying a remote link by its
URL — an agent told `false` learns that an earlier call of its own already landed — and it is a
field rather than a second `outcome`, so that "did this work" stays one comparison against `ok`
rather than a set of values that grows with every tool. The URL is carried because it is the link's
identity; the title and the relationship are text a human typed, and they are not.

## Workflow prompts

Beside its tools the server carries one **workflow prompt** — a procedure your client surfaces for
you to pick, usually as a slash command. Prompts are user-initiated by the protocol: an agent
already in mid-loop cannot fetch one for itself, so what a prompt saves is typing at the start of a
session, not a step in an unattended run. That is the whole of what it claims (ADR-0011).

| Prompt | Argument | Requires | What it does |
|---|---|---|---|
| `implement_issue` | `key`, optional | `jira_get_issues`, `jira_transition_issue`, `jira_add_comment` | Hands one issue to the agent end to end: read it, take it by whichever transition this workflow uses for work in progress, do the work, comment the outcome. Without `key` it starts from `jira_my_open_issues`. |

A prompt is registered only where every tool its procedure names is registered, so a read-only
client sees no prompts at all — it could not follow this one. The gate is derived from the tool
surface rather than declared again, which is why the table above names tools and not grants.

The procedure names no status. Status vocabulary is per-team and this server does not know yours,
so the agent is told to read the issue's own transitions and take the one that means work started.
Nothing in the message is fetched from Jira: it is static text, so it cannot go stale and carries
no Jira-authored content.

## Example prompts

These are prompts you type at the agent, not commands you run — the server exposes no prompt of its
own, and every one of them is an ordinary sentence that happens to be answerable with the tools
above. Each example names the tool chain it tends to drive, so you can tell a prompt that costs one
call from one that costs several. The chains are the typical path, not a guarantee: which tools a
model reaches for is the model's decision, and a different model may take a different route to the
same answer.

Two things decide whether a prompt works at all. **Writes need the matching grant** — a prompt that
asks for a comment against a server launched without `--allow comments:write` fails by the tool not
existing, and the agent will say so rather than half-doing it. **The Jira Software prompts need a
Jira Software licence**, because the board, sprint and backlog tools are registered only where the
capability probe found one.

### Orientation

Cheap prompts, worth making the first ones of a session — they establish which account the server
is acting as and what it can see, which is the context every later answer depends on.

- *"Which Jira account am I connected as, and what is the server URL?"* — `jira_whoami`.
- *"List every Jira project I can see, and tell me which ones are software projects."* —
  `jira_list_projects`, whose response carries the project type per row.
- *"Is there a project whose name mentions payments? Give me its key."* — `jira_list_projects`, then
  the agent filters locally rather than guessing a key.
- *"What are the issue types and workflow statuses in PROJ?"* — `jira_get_project`, which returns
  issue types, statuses, components and versions in one response.
- *"Find the Jira username for Jane Bloggs — I need it for an assignee field."* —
  `jira_search_users`. On Jira Server the answer is a username, not a Cloud account identifier, and
  that is the value the write tools want.

### The work queue

- *"What am I working on? List my open issues, most recently touched first."* —
  `jira_my_open_issues`, one call, no JQL to author.
- *"Of my open issues, which have had no update in more than two weeks?"* — `jira_my_open_issues`,
  then the agent compares the update timestamps it already has rather than searching again.
- *"Give me a standup summary: what I have in progress, what is blocked, and what I closed
  yesterday."* — `jira_my_open_issues` for the first two, plus a `jira_search` with a
  `resolutiondate` clause for the third.
- *"Which of my issues are assigned to me but sitting in a status someone else owns?"* —
  `jira_my_open_issues`, then `jira_get_issues` with `include: ["transitions"]` to see which moves
  are actually available to this account.

### Waking on a change

A workflow that runs on a schedule needs to know what moved since it last looked. `jira_changed_since`
owns the query, the zone that query is read in, and the ordering; what the loop carries between
ticks is one timestamp.

- *"Every fifteen minutes, tell me what changed in PROJ and act on anything assigned to me."* —
  `jira_changed_since` with `project: "PROJ"`, then the loop passes the `nextSince` it returned
  back in on the next tick:

  ```
  tick 1  jira_changed_since(since: "2026-08-18T09:00:00+02:00", project: "PROJ")
          → PROJ-12 changed at 09:14 — nextSince: "2026-08-18T09:14:00+02:00"
  tick 2  jira_changed_since(since: "2026-08-18T09:14:00+02:00", project: "PROJ")
          → nothing newer — PROJ-12 again, nextSince: "2026-08-18T09:14:00+02:00"
  tick 3  jira_changed_since(since: "2026-08-18T09:14:00+02:00", project: "PROJ")
          → PROJ-13 changed at 09:41 — nextSince: "2026-08-18T09:41:00+02:00"
  ```

  The watermark tracks the last change seen, not the clock. It does not advance on a quiet tick, so
  the most recent change is reported again until something newer arrives — a non-empty result is not
  by itself news, and an agent deciding whether to act compares the keys against the ones it has
  already handled.

  `nextSince` is the start of the last-seen minute, not the exact moment of the last change, because
  Jira Server records update times to the minute on some versions. The feed therefore repeats rather
  than skips. An agent holding the keys it has seen can recognise a repeat; nothing can recognise a
  change that never arrived.

  Where a result carries `nextStartAt`, this window holds more than one page — a bulk edit puts
  hundreds of issues in one minute. Read it out with `startAt` before moving on to `nextSince`,
  which does not advance past a window still being read.

  `since` must carry an offset. One without is refused rather than read in this server's zone. The
  window itself is stated in the time zone of the Jira account this server is authenticated as,
  because that is the zone Jira reads a date in a JQL clause in — the mistake this tool exists to
  take off the caller.

### Searching

`jira_search` takes JQL, but you rarely write it — describing the query in English and letting the
agent author the JQL is the point, and asking it to show you the JQL it used is a good habit.

- *"Find every open bug in PROJ with priority Highest, newest first."* — `jira_search` with
  `project = PROJ AND type = Bug AND priority = Highest AND resolution = EMPTY ORDER BY created DESC`.
- *"How many unresolved issues does PROJ have? I only want the number."* — `jira_search` returns the
  total alongside the page, so a count needs no paging.
- *"Show me everything in PROJ that changed in the last 48 hours, and who changed it."* —
  `jira_search` with `updated >= -48h`, then `jira_get_issues` with `include: ["changelog"]` for the
  ones worth explaining.
- *"Which issues in PROJ are unassigned and have been open longer than 30 days?"* — `jira_search`
  with `assignee IS EMPTY AND created <= -30d`.
- *"List issues in PROJ with the label `tech-debt` across every status, and group them by
  component."* — `jira_search`, with the grouping done by the agent over the projected fields.
- *"Search PROJ for anything mentioning the phrase 'rate limit' in the summary or description."* —
  `jira_search` with a `text ~` clause.
- *"Show me the JQL you would use for 'issues I reported that someone else resolved this
  quarter' — don't run it yet."* — no call at all. Useful for checking a query before it touches a
  large instance.

### Reading issues in depth

`jira_get_issues` takes up to 20 keys at once and each `include` key succeeds or fails on its own,
which is what makes "and their comments" cost one call instead of twenty.

- *"Summarise PROJ-123: what is it, who owns it, and where is it stuck?"* — `jira_get_issues` for
  the one key.
- *"Read PROJ-123, PROJ-124 and PROJ-131 and tell me whether they are describing the same bug."* —
  one `jira_get_issues` call with all three keys.
- *"What did the last comment on each of my open issues say?"* — `jira_my_open_issues`, then
  `jira_get_issues` with `include: ["comments"]` in a single batched call.
- *"Show me PROJ-123's full history — who changed the status, and when."* — `jira_get_issues` with
  `include: ["changelog"]`.
- *"What can PROJ-123 transition to right now?"* — `jira_get_issues` with `include:
  ["transitions"]`, which resolves against this account's permissions rather than the workflow
  diagram.
- *"What is PROJ-123 blocked by, and are those blockers still open?"* — `jira_get_issues` with
  `include: ["links"]`, then a second `jira_get_issues` for the linked keys.
- *"How much time has been logged against PROJ-123, and by whom?"* — `jira_get_issues` with
  `include: ["worklogs"]`.
- *"For PROJ-123, PROJ-124 and PROJ-125, give me a table of summary, assignee, status and last
  comment date."* — one `jira_get_issues` call with `include: ["comments"]`.

### Attachments

On a legacy Jira the specification is regularly a file on the ticket rather than the description
field. Listing the files is an expansion; reading one is a tool of its own, because a file is worth
far more context than a section of an issue read should ever spend.

- *"PROJ-123 says 'see attached'. What does the attachment actually say?"* — `jira_get_issues` with
  `include: ["attachments"]` for the name, size and identifier, then `jira_get_attachment` with
  that identifier.
- *"Read the 400 KB server log on PROJ-123 and find the first stack trace."* —
  `jira_get_attachment`, then again with the `nextOffset` it returned, and again, until there is no
  `nextOffset` left. Each call is one window of the file. Where Jira reports no size for an
  attachment — some instances do not — a full window is reported as "probably more" rather than as
  the end of the file, and `size` and `bytesRemaining` are absent rather than zero.

Two things this deliberately does not do. It does not trust the media type: whether a file is
readable is decided by inspecting its bytes, because instances of the vintage this project targets
label plain text as `application/octet-stream` and binaries as `text/plain` often enough that a
rule built on the label fails exactly where it matters. Jira's claim is still reported, marked as a
claim. And it never inlines a binary — a screenshot or a ZIP is described, with its name, size and
claimed type, because its bytes would cost an agent its context to learn nothing. The whole of a
window is checked rather than a sample of its front, so a file that is text for a page and binary
after it is not half inlined.

An attachment is the least trustworthy text on a ticket: anyone with a Jira account can put a file
there. It is delimited as untrusted content on the way out, always, with no case analysis about
where it came from.

### Boards, sprints and the backlog

**Jira Software only.** On a Jira Core instance these four tools are not registered, and the agent
will tell you the capability is absent rather than failing four calls.

- *"Which Jira boards can I see?"* — `jira_list_boards`.
- *"What sprint is the PROJ board in right now, and when does it end?"* — `jira_list_boards`, then
  `jira_list_sprints`.
- *"List everything in the active sprint on the PROJ board, grouped by assignee."* —
  `jira_list_boards`, `jira_list_sprints`, `jira_get_sprint_issues`.
- *"How is the current sprint doing? Give me a count by status and flag anything still in To Do."* —
  the same chain, with the arithmetic done over the returned issues.
- *"Which issues in the active sprint are unassigned or have no story point estimate?"* —
  `jira_get_sprint_issues`, then the agent reads the projected fields.
- *"What is at the top of the PROJ backlog, and is any of it ready to pull in?"* —
  `jira_get_backlog`, then `jira_get_issues` for the top few.
- *"Compare the last two closed sprints on the PROJ board: how many issues did each finish?"* —
  `jira_list_sprints`, then `jira_get_sprint_issues` per sprint.

### Preparing a create

Worth its own prompts, because a create that skips this step is the create most likely to be
rejected by a required custom field the agent never saw.

- *"What fields does a Bug in PROJ require? I want to know before you create anything."* —
  `jira_get_create_fields`, which returns every field with its identifier, type, whether it is
  required, and its allowed values.
- *"Does PROJ have any mandatory custom fields on Story creation? Show me their allowed values."* —
  `jira_get_create_fields` for that project and issue type.
- *"Draft the issue you would create for this bug report, show me the exact field map, and wait for
  my approval before creating it."* — `jira_get_create_fields` and nothing else until you say go.

### Writing — `issues:write`

Launch with `--allow issues:write`. Without it these tools are not registered at all, so the prompt
fails by absence rather than by a permission error.

- *"Create a Bug in PROJ: summary 'Checkout fails on expired card', with the reproduction steps from
  my last message in the description."* — `jira_get_create_fields`, then `jira_create_issue`.
- *"File this failing test as a Task in PROJ, set priority High, and assign it to me."* —
  `jira_get_create_fields`, `jira_whoami` for the username, `jira_create_issue`.
- *"Assign PROJ-123 to Jane Bloggs."* — `jira_search_users` for the username, then
  `jira_update_issue`. There is no separate assign tool; the assignee is a field.
- *"Update PROJ-123: set the fix version to 2.4.0 and add the label `regression`."* —
  `jira_get_project` to confirm the version exists, then `jira_update_issue`.
- *"Rewrite PROJ-123's description to include the stack trace I just pasted, keeping what is already
  there."* — `jira_get_issues` to read the current description, then `jira_update_issue`.
- *"Move PROJ-123 to In Review."* — `jira_get_issues` with `include: ["transitions"]`, then
  `jira_transition_issue` by transition name.
- *"Close PROJ-123 as Won't Do with a comment explaining why."* — `jira_transition_issue`, which
  takes an optional comment and any screen fields the transition demands, in the one call.
- *"Take everything I have in To Do on the current sprint and move it to In Progress."* —
  `jira_get_sprint_issues`, then one `jira_transition_issue` per issue. There is no bulk write, so
  ask the agent to list what it will change before it changes it.

### Canned queries of your own

Every team has three or four queries of its own — this team's bugs in the current sprint, the
release-blocking issues, the tickets waiting on review — and none of them belong in this
repository. A profile can carry them, and each becomes a tool:

```
jira-server-mcp profile query add work sprint_bugs \
  --jql "project = PROJ AND type = Bug AND sprint in openSprints()" \
  --description "This team's bugs in the current sprint."

jira-server-mcp profile query list work
jira-server-mcp profile query remove work sprint_bugs
```

The agent then calls `jira_q_sprint_bugs` and spends no context authoring the query.

- **The `jira_q_` prefix is fixed.** An operator-supplied name can never shadow or collide with a
  built-in tool, and an agent can see at a glance which tools belong to this deployment rather than
  to this server. The cost is a longer name in every call, which is the cheaper half of that trade.
- **The JQL is checked when you declare it.** `profile query add` runs it against Jira and refuses
  to store one Jira rejects, so a mistake lands in front of the person who wrote it. A query that
  goes bad later — a project deleted, a field removed — fails at call time like any other refused
  request.
- **A description is required.** It is what an agent reads when choosing between tools, and a
  generated one would be a lie about intent only you know.
- **No parameters.** A query whose meaning changes with an argument is `jira_search`'s job. These
  take paging and a field projection, and nothing else.
- **Ten per profile, and that is a limit rather than advice.** Every registered tool costs an agent
  context in *every* conversation it takes part in — the tool list is sent whether or not any tool
  is called. This is the one place a deployment could quietly spend the budget this whole project
  exists to protect, so the eleventh is refused with the reason.

A query result is rendered by the same module, under the same response budget, and carries the same
structured half as a built-in page of issues.

### Field aliases

A custom field is named only by identifier everywhere an agent touches it: `customfield_10010` in a
create, in an update, in the extra fields of a read. The identifier differs per instance, so a
workflow that sets story points against a second Jira is otherwise a different workflow — and
`jira_get_create_fields` answering with the field's name fixes discovery at the cost of a round trip
per workflow, in an answer that does not survive a compacted conversation.

Declare the names you want on the profile:

```
jira-server-mcp profile alias set work story_points customfield_10010
jira-server-mcp profile alias set work severity     customfield_10021
jira-server-mcp profile alias list work
jira-server-mcp profile alias remove work severity
```

An alias is an **additional** name, never a rename:

- **Writes and field projections accept either.** `jira_create_issue`, `jira_update_issue` and the
  `fields` argument of every read take `story_points` or `customfield_10010` interchangeably.
- **Reads show both**, as `story_points (customfield_10010)`. Replacing the identifier would hide
  the value an agent still needs for everything an alias does not cover.
- **A rejected write names the aliases this profile declares.** A field name this server does not
  recognise is passed to Jira unchanged — the field catalogue lives in Jira, not here, and an
  unfamiliar name is far more likely to be a real identifier than a mistake. So the moment an
  unknown name fails is the moment Jira refuses it, and that failure carries the aliases it could
  have used.

Aliases are declared, never derived from Jira's own field names: two instances would otherwise get
the same alias by accident, which is a trap rather than a contract. A name spelled like a field
identifier — `customfield_10010` — is refused as an alias, since the two would be ambiguous.

### Retry-safe writes

Three writes have no natural way to be repeated safely: a create, a comment and a worklog. If one
times out, Jira may or may not have committed it, and nothing in the answer says which. Today that
leaves an agent to invent a recovery procedure in the worst possible context — after a timeout,
mid-workflow — and an unattended loop either duplicates the work or stalls.

`jira_create_issue`, `jira_add_comment` and `jira_add_worklog` each take an optional
`idempotencyKey`: any string the caller invents, one per intended write.

```
jira_create_issue(projectKey: "PROJ", issueType: "Bug", summary: "…", idempotencyKey: "run-42-step-1")
→ Jira does not answer in time
jira_create_issue(projectKey: "PROJ", issueType: "Bug", summary: "…", idempotencyKey: "run-42-step-1")
→ "This key was already used by a create whose outcome is unknown … Nothing was written again."
```

The key is recorded **before** the write is sent, which is the whole point. Recording the outcome
afterwards would help only when the first call came back — and a duplicate arises precisely when it
did not. Recording on the way out means a repeat knows an attempt was made even when nothing is
known about how it ended, so the server remembers and the agent does not have to.

What the second call is told depends on how the first ended, because what it may do next differs:

- **It succeeded** — the answer names what it produced and is not an error. A loop repeating a step
  wants "that is already done".
- **Its outcome is unknown** — it was sent and nothing came back. Nothing is written again, and the
  answer says how to find out what happened.
- **Jira rejected it** — nothing was written then either, and a key names one attempt rather than
  one intention, so the corrected call needs a new key. Only an answer that proves Jira did not act
  counts as a rejection: a 502 or a 504 from something in front of Jira leaves the outcome unknown,
  because the write may well have landed behind it.

Two limits, stated rather than hidden. The server does not search Jira to find out what the first
attempt did: that is a different search for each of the three writes, and a comment has no reliable
search handle at all. And the record lives in memory for the life of the server process — a
restarted loop is back to reading Jira, which is why every one of these tools still carries that
advice for when there is no key.

`jira_update_issue` and `jira_transition_issue` take no key: repeating them changes nothing beyond
an extra audit-trail entry. `jira_add_remote_link` takes none either — it is keyed by the URL, which
is Jira's own idempotency and better than anything this server could add.

### Writing — `comments:write`

Launch with `--allow comments:write`. Comments are Jira wiki markup and are stored exactly as
written.

- *"Comment on PROJ-123 that the fix is merged and name the commit."* — `jira_add_comment`.
- *"Read PROJ-123, then post a comment summarising what we decided in this conversation."* —
  `jira_get_issues`, then `jira_add_comment`.
- *"Draft a comment for PROJ-123 asking the reporter for their browser version — show it to me
  first."* — nothing until you approve, then `jira_add_comment`.
- *"Post the same status note on PROJ-123, PROJ-124 and PROJ-131."* — three `jira_add_comment`
  calls, one per issue.

### Writing — `worklogs:write`

Launch with `--allow worklogs:write`. Time is Jira's own duration syntax, so how long a working day
is stays Jira's decision, not this server's.

- *"Log 3h 30m against PROJ-123 for today, with the comment 'pairing on the retry logic'."* —
  `jira_add_worklog`.
- *"I spent yesterday afternoon on PROJ-124 — log 4h against it, dated yesterday."* —
  `jira_add_worklog` with an explicit start date.
- *"How much have I logged against PROJ-123 so far, and add another 45m?"* — `jira_get_issues` with
  `include: ["worklogs"]`, then `jira_add_worklog`.

### Writing — `links:write`

Launch with `--allow links:write`. The relation is Jira's own wording, so the prompt and the tool
call read the same way round.

- *"Link PROJ-123 as blocking PROJ-124."* — `jira_link_issues` with `relation: "blocks"`.
- *"PROJ-123 is blocked by PROJ-124 — record that, and say why in a comment on the link."* —
  `jira_link_issues` with `relation: "is blocked by"` and a comment, in the one call.
- *"Attach this pull request to PROJ-123 so it shows in the link panel."* —
  `jira_add_remote_link` with `relationship: "pull request"`. Attaching the same URL again updates
  that link rather than adding a second.
- *"What is blocking PROJ-123, and is there a PR on it yet?"* — `jira_get_issues` with
  `include: ["links"]`, which returns both the issue links and the remote links.

### Multi-step workflows

Where the batching actually pays: each of these is one prompt that would otherwise be a dozen
browser tabs.

- *"Give me a morning briefing: my open issues, which changed since yesterday, and the last comment
  on each of those."* — `jira_my_open_issues`, then one `jira_get_issues` with
  `include: ["comments"]` for the subset that moved.
- *"Prepare me for sprint review on the PROJ board: what finished, what did not, and for each
  unfinished issue the last comment explaining why."* — `jira_list_boards`, `jira_list_sprints`,
  `jira_get_sprint_issues`, then `jira_get_issues` with `include: ["comments"]`.
- *"Triage PROJ: find unassigned bugs opened in the last week, read each one, and suggest an
  assignee and priority — propose, don't apply."* — `jira_search`, then `jira_get_issues`, and no
  write tool until you say so.
- *"This stack trace matches PROJ-123. Confirm it against the issue, then comment with the analysis
  and move it to In Progress."* — `jira_get_issues`, `jira_add_comment`, `jira_transition_issue`,
  which needs both `comments:write` and `issues:write`.
- *"You have finished PROJ-123 — attach the pull request you opened, move it to In Review, and
  comment with what changed."* — `jira_add_remote_link`, `jira_transition_issue`,
  `jira_add_comment`, which needs `links:write`, `issues:write` and `comments:write`.
- *"Audit the PROJ backlog: which items have no description, no component, or no estimate?"* —
  `jira_get_backlog`, then `jira_get_issues` for the details the backlog view omits.
- *"I am picking up PROJ-123. Assign it to me, move it to In Progress, and comment that I have
  started."* — `jira_whoami`, `jira_update_issue`, `jira_transition_issue`, `jira_add_comment`.

### Prompts that will not work

Named here so the failure is expected rather than surprising. Each is a
[known limitation](#known-limitations), not a bug.

- *"Delete PROJ-123."* — no delete tool exists at any grant.
- *"Attach this log file to PROJ-123."* — no attachment upload or download.
- *"Move PROJ-123 into the next sprint"* or *"create a sprint."* — sprints and boards are read-only
  here.
- *"Edit my last comment on PROJ-123."* — comments cannot be edited or removed through this server.
- *"Do the same in our other Jira."* — a process serves exactly one profile, and no tool takes an
  instance argument. Run a second server against the second profile instead.

## Security model

The short version: your token stays in your operating system's credential store, this server has
exactly the Jira permissions of the account that issued the token, and it can write only what you
granted at launch.

- **The token.** Kept in the OS credential store — Keychain on macOS, Credential Manager on
  Windows, Secret Service on Linux. Never in `profiles.json`, never in a log line, never in an
  exception message, never in an argument. A test captures a full log of an authenticated round
  trip and asserts the token appears nowhere in it.
- **The encrypted file store, honestly.** Headless Linux, WSL, an SSH session and a KDE desktop
  with no Secret Service bridge have no keyring to reach, so there is a fallback: an AES-GCM
  encrypted file, mode `0600`, its key protected by DPAPI on Windows and by a `0600` machine key
  file elsewhere. **That key sits on the same disk as the ciphertext.** It stops another account's
  casual read and a backup from containing a plaintext token. It does not stop anything running as
  you. Nothing local can — but the OS store's protection is stronger, so the file store is opt-in
  (`--credential-store file`) or automatic only where no OS store answers.
- **Transport.** HTTPS is required; only an explicit loopback address is exempt. There is no
  `--insecure` switch, because it would be pasted into every teammate's configuration within a
  week. A private certificate authority is trusted explicitly, per profile, with
  `--ca-bundle <path>`. Redirects are disabled: a redirect is an error, not a hop to another host.
- **No instance in the schema.** The base URL comes from `profiles.json` and nothing else. No tool
  takes a URL, a host, or a path segment, so an agent cannot point this server somewhere new.
- **Writes.** Only under a grant, and a tool without its grant is not registered at all. No delete
  tool exists at any grant.
- **Prompt injection.** Jira content is text anyone with a Jira account can write, and it reaches a
  model. Every piece of Jira-authored free text is wrapped in explicit delimiters, carrying a
  marker chosen afresh for each response so content cannot close the region early, and labelled as
  data that must not be treated as instructions. Content is **not** scanned, stripped or
  sanitized: pattern-matching mangles legitimate text, guarantees nothing, and would censor an
  issue that is legitimately *about* prompt injection. The real bound on the blast radius is not
  the text — it is the grant you gave. An injected instruction to delete a project cannot be
  followed, because no such tool is registered under any grant; an injected instruction to comment
  can only be followed by a client you granted `comments:write`. Grant the least that is useful,
  and read what the agent proposes to write.
- **What this does not defend against.** Malware already running as you on your own machine. It
  can read your keyring, and no local credential store changes that. This protects a developer's
  Jira token on a machine the developer controls, which is the threat model it claims and the whole
  of it.

## Troubleshooting

**`401` — the token is invalid or revoked.** Tools report which profile, and
`jira-server-mcp auth status <profile>` says the same. There is nothing to retry: create a new
token in Jira and run `jira-server-mcp auth login <profile>`.

**`403` — the account lacks the Jira permission.** The message names the operation and the
endpoint. This server does not model Jira's permission scheme and does not retry a `403`. Check
which account it is with `jira_whoami`, then check that account's permissions in Jira.

**`404` — which means two different things.** Jira answers `404` both when something does not
exist and when it exists but your account cannot see it, and it does not distinguish them. So a
`404` on an issue or a project means "no such thing *that this account can see*": check the key,
then check whether the account has Browse Projects on it. A `404` on *every* request instead means
the base URL is wrong — most often a missing context path, `https://jira.example.com/jira` rather
than `https://jira.example.com`. Fix that with `jira-server-mcp profile add` again.

**A private certificate authority.** An internal Jira behind a private root fails with a TLS
error. Point the profile at the bundle: `jira-server-mcp profile add work --url … --ca-bundle
/path/to/ca.pem`. The bundle is PEM, may hold several certificates, and is read by every verb and
by `serve`. Do not look for `--insecure`; there is none.

**No keyring on headless Linux.** `secret-tool` reports
`The name org.freedesktop.secrets was not provided by any .service files` where there is no
Secret Service — a headless box, WSL, an SSH session, some KDE desktops. The tool says so once and
falls back to the encrypted file store. To choose it explicitly, pass `--credential-store file` to
`auth login` **and** to `serve`; the caveat above applies. `--credential-store native` refuses to
fall back, which is what you want in a script that must not silently write a weaker store.

**The Jira Software tools are missing.** They are registered only where the capability probe
recorded a licence, and the probe is taken at `auth login` and cached on the profile with a 7-day
lifetime. If Jira Software has since been licensed — or the probe was never taken — run
`jira-server-mcp profile refresh <profile>` and restart the client. On a Jira Core instance the
absence is correct and permanent: the whole software API answers `404` there, and registering four
tools that always fail would only invite the model to try them. Startup performs no network calls,
so a stale probe is a stale tool list rather than a server that will not start; the server says as
much on stderr when it starts.

## Development

```
git clone https://github.com/matthewrosse/jira-server-mcp.git
cd jira-server-mcp
dotnet build
dotnet test tests/JiraServerMcp.Tests
```

The .NET 10 SDK version is pinned in `global.json`; tests run on Microsoft Testing Platform. Two
source projects, one dependency edge: `JiraServerMcp` (MCP host, tools, CLI verbs, profiles,
credential stores) references `JiraServerMcp.Jira` (typed HTTP client, models, paging, error
mapping, capability probe), and the client knows nothing about MCP (ADR-0003). The design
specification is `docs/design/architecture.md`, the vocabulary is `CONTEXT.md`, and the five
decisions that are hard to reverse are in `docs/adr/`.

### The test tiers

Four projects, and one trait — `Category=JiraIntegration` — that decides what needs Docker:

```
dotnet test tests/JiraServerMcp.Jira.Tests        # models, paging, resilience, WireMock on the wire
dotnet test tests/JiraServerMcp.Tests            # tools, CLI verbs, credential stores, profiles, rendering
dotnet test tests/JiraServerMcp.Protocol.Tests   # a real MCP client against a real server, Jira stubbed
dotnet test tests/JiraServerMcp.JiraIntegration.Tests \
  -- --filter-trait "Category=JiraIntegration"   # against a real Jira 8.20.7 — needs Docker
```

The first three need nothing but the SDK: the first two take seconds, and the protocol tier a
couple of minutes, since every case starts a server. The fourth provisions a Jira, which took 12
minutes end to end on the slowest machine this has run on. To run everything that needs no Docker,
including the harness's own parser and readiness tests:

```
dotnet test tests/JiraServerMcp.JiraIntegration.Tests -- --filter-not-trait "Category=JiraIntegration"
```

### Running the canonical Jira locally

```
./scripts/jira-up.sh      # Jira 8.20.7 and Postgres, set up, licensed, seeded, suite run
./scripts/jira-down.sh    # containers and volumes
```

One command from an empty Docker host to an authenticated instance. The testing licence expires
**three hours** after it is applied, which is longer than any run and shorter than a working day.
`tests/README.md` documents the harness, what the Phase 0 spike established, what to do when
Jira's setup wizard changes shape between versions, and how to refresh the licence.

### How the tests run in CI

- **`ci.yml`** — every pull request and every push to `main`. Restore, build with warnings as
  errors, `dotnet format --verify-no-changes`, dependency audit, and every test project with
  `--filter-not-trait Category=JiraIntegration`, plus a coverage summary. A second job exercises
  the credential store contract against the genuine Keychain, Credential Manager and Secret
  Service on macOS, Windows and Linux runners, and fails if a single test skipped — a skip there
  would mean the real backend was never reached.
- **`jira-integration.yml`** — nightly, on `workflow_dispatch`, and on a pull request labelled
  `run-jira-tests`. Never on an ordinary push: minutes of Jira boot on every commit, many times a
  day, for a signal that changes rarely. It brings up Jira 8.20.7 through Testcontainers, runs the
  trait-gated suite, and uploads Jira's own logs when it fails.
- **`release.yml`** — on a `v*` tag. Below.

No workflow needs a secret or a purchased licence.

## Packaging and publishing

A tag is the only place a version is written. `release.yml`, on `v<major>.<minor>.<patch>`, reads
the version from the tag, refuses a tag that does not name one, and passes it to both the pack and
the six publishes — which is what keeps the tool package and the binaries agreeing about their own
version:

```
git tag v0.1.0
git push origin v0.1.0
```

It then packs the tool, smoke-tests the packed tool by installing it and running two verbs,
cross-publishes the six self-contained binaries from one runner, checksums them, attaches build
provenance attestations, pushes the package to GitHub Packages, and drafts a GitHub release with
generated notes. The release is a draft: publishing it is a human decision. Nothing reaches the
feed until every artefact has been built and started, so a half-releasable tag leaves the feed
untouched.

nuget.org publication is deferred until the tool surface stops moving, and will use trusted
publishing over OIDC rather than a long-lived API key when it happens.

## Compatibility notes

- Everything targets `/rest/api/2`, pinned. There is no `/rest/api/3` on Jira Server, and
  `/rest/api/latest` is a moving alias that would make behaviour depend on the deployment.
- Boards, sprints and the backlog come from `/rest/agile/1.0`, which exists only where Jira
  Software is licensed. Its absence is normal, not an error.
- The capability probe — version, deployment type, Jira Software licensed — is taken once, at
  `auth login`, cached on the profile for seven days, and refreshed by `profile refresh`. Startup
  does no network I/O, so the MCP handshake never waits on Jira.
- Version-conditional behaviour reads the probe, in one place. In the current surface there is
  exactly one such condition: whether Jira Software is licensed.
- Even under a Data Center licence, a single node reports `"deploymentType": "Server"`. Deployment
  type does not tell you which licence is in play.
- Jira Server identifies users by `name` and `key`. Cloud's `accountId` does not exist here.

## Known limitations

What is **not** in this server, so you find out here rather than by asking an agent to try:

- **Attachments.** No upload, no download. File paths crossing the MCP boundary are a
  path-traversal surface that deserves its own design pass and its own ADR.
- **Deletion.** No delete tool of any kind, at any grant. Not issues, not comments, not worklogs.
- **Comment and worklog editing.** A comment or worklog this server adds cannot be edited or
  removed through it.
- **Unlinking.** Links are made with `links:write` and read through `jira_get_issues`' `links`
  expansion, but nothing removes one. A remote link is keyed by its URL, so the ordinary
  correction — the same pull request attached again — updates the one link instead; a wrong issue
  link is a human's cleanup in Jira.
- **Sprint mutation.** Sprints and boards are read-only here: no creating a sprint, no moving an
  issue into one.
- **Watchers, votes, and bulk operations.** None of them — "bulk operations" here means bulk
  *write*. Bulk read exists: `jira_get_issues`.
- **OAuth 1.0a.** Personal access tokens are the only credential (ADR-0001), which sets the floor
  at Jira 8.14 and needs no administrator. OAuth 1.0a on Jira Server requires an administrator to
  provision an application link, and the question of who holds the private key has no acceptable
  answer for a tool meant to be installed in a minute.
- **HTTP transport.** stdio only (ADR-0002). A remotely hosted server has to answer "whose Jira
  credentials should this request use", and that is an authorization design, not a transport
  change. The tools carry no transport knowledge, so it stays additive work.
- **Basic authentication.** Not supported, so Jira below 8.14 is not supported either.
- **MCP resources and prompts.** Tools only. Everything here is either a parameterised query or
  content that changes under the agent's feet, and neither is what a resource is good at.

Each of these is additive, and the write path having real usage is what should decide the order.

## Licence

MIT. See [LICENSE](LICENSE).
