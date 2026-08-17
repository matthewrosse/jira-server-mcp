# jira-server-mcp

An MCP server that gives a coding agent read and gated write access to a legacy, self-hosted
**Jira Server** — Server or Data Center, 8.14 and later. **Not Jira Cloud:** Cloud is a different
REST API with a different authentication model, and it is out of scope here. If your Jira lives at
`something.atlassian.net`, this is the wrong tool. If it lives on a host your organisation runs,
this is the right one.

It speaks the Model Context Protocol over stdio, authenticates with a **personal access token**
kept in your operating system's **credential store**, serves exactly one **profile** — one named
Jira Server — per process, and registers a write tool only where you handed that client the
matching **grant**. Sixteen tools, sized for an agent's context rather than mapped one-to-one onto
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
| `jira_get_issues` | — | Up to 20 issues in one call, with `include` opting into `comments`, `transitions`, `changelog`, `links` and `worklogs` for each. Each key succeeds or fails on its own, and the transition list arrives with the issue the agent is about to transition. |
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

Deliberately absent: issue deletion, comment editing and deletion, attachments, issue linking,
sprint mutation, watchers, and votes. See [Known limitations](#known-limitations).

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
- **Issue linking.** No link, unlink, or link-type tools. Links are readable through
  `jira_get_issues`' `links` expansion.
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
