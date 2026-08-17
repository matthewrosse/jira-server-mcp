# jira-server-mcp — architecture and implementation plan

Design specification produced from the grilling session of 2026-08-13. Decisions recorded here
were made deliberately; the five that are hard to reverse have their own ADRs in `docs/adr/`.

Every claim below is marked as one of:

- **[fact]** — verified against documentation, a package feed, or a command run on this machine.
- **[decision]** — chosen in the design session, with the reasoning attached.
- **[assumption]** — believed true, not verified, cheap to correct if wrong.
- **[unverified]** — must be validated experimentally before it is relied upon. All of these are
  concentrated in Phase 0.

---

## 1. Executive summary

`jira-server-mcp` is a stdio MCP server that gives coding agents read and gated write access to a
self-hosted Jira 8.14+, targeting Jira 8.20.7 as both the primary production instance and the
canonical integration-test version.

The five decisions that shape everything else:

1. **Personal access tokens are the only credential** (ADR-0001). OAuth 1.0a is dropped, and with
   it the browser callback, the RSA keypair, the application-link provisioning, and their entire
   threat surface. The floor becomes Jira 8.14.
2. **stdio only** (ADR-0002). One process, one user, no listener, no authorization layer.
3. **Two source projects, not five layers** (ADR-0003). One real boundary: the Jira client knows
   nothing about MCP.
4. **A .NET tool plus self-contained binaries; never a Docker image for the server** (ADR-0004).
   Docker cannot reach the host credential store, and the credential store is the whole point.
5. **One profile per process, selected by launch arguments** (ADR-0005). An agent cannot address
   the wrong Jira, because it cannot address a Jira at all.

The tool surface is agent-oriented rather than a mapping of REST endpoints: sixteen tools, one of
which — `jira_get_issue` — subsumes what would otherwise be five, through opt-in expansions.
Reads are on by default; writes are registered only when the operator grants a category, so a
forbidden operation is invisible rather than merely refused.

---

## 2. Requirements

### Functional

- Expose Jira issue search, issue read, project and metadata read, and user lookup as MCP tools.
- Expose issue creation, update, transition, commenting, and worklog logging, each behind a grant.
- Expose Jira Software boards, sprints, sprint issues, and backlog where Jira Software is
  licensed, and not at all where it is not.
- Manage several Jira Servers as named profiles from a single installation.
- Acquire, validate, store, inspect, and delete a personal access token per profile from a CLI.

### Non-functional

- Server startup performs no network I/O; the MCP handshake must not wait on Jira.
- A default issue read costs the agent hundreds of tokens, not tens of thousands.
- Cancellation propagates from the MCP call to the in-flight HTTP request.
- Runs on Windows, macOS, and Linux, x64 and arm64, including headless Linux and WSL.
- Maintainable by one experienced .NET developer: two source projects, no speculative
  abstractions, no framework of our own.

### Security

- The token never appears in a log, an exception message, a process argument, or a configuration
  file.
- Secrets live in the OS credential store; the fallback file store is opt-in, `0600`, encrypted,
  and honestly documented as weaker.
- Credentials are isolated per profile, and per process.
- TLS is mandatory except for explicit `http://localhost`; there is no verification-off switch.
- The Jira base URL comes only from local configuration, never from a tool argument.
- Writes are impossible without an explicit grant; issue deletion is impossible at all.
- Jira-authored content reaching the model is marked as untrusted data.

### Compatibility

- Floor: Jira Core / Jira Software 8.14 [fact — PATs were introduced in 8.14, Server and Data
  Center alike].
- Canonical: 8.20.7 [fact — the user's production build is `v8.20.7#820007-sha1:98f6b16`, and
  `atlassian/jira-software:8.20.7` exists on Docker Hub].
- Expected to work, untested: Jira 8.14–8.22, Jira 9.x and 10.x Data Center [assumption — the
  `/rest/api/2` surface used here is stable across those versions]. The README states tested
  versus expected, and never claims a version CI has not run.
- Jira Cloud is explicitly out of scope.

### Developer experience

- `dotnet tool install`, `profile add`, `auth login`, paste the config snippet — under five
  minutes with no administrator involved.
- One `docker compose up` for a real Jira 8.20.7 locally.
- A pull request's checks finish in about three minutes; Jira-backed tests run elsewhere.

---

## 3. Architecture

```
MCP client (Claude Code, VS Code / Copilot)
        │  stdio, JSON-RPC
        ▼
JiraServerMcp                       ← MCP host, tools, CLI verbs, profiles, credential store
        │  method calls, typed models
        ▼
JiraServerMcp.Jira                  ← typed HttpClient, paging, error mapping, capabilities
        │  HttpClient + DelegatingHandlers (auth, resilience, logging)
        ▼
Jira Server 8.20.7   /rest/api/2 and /rest/agile/1.0
```

**`JiraServerMcp.Jira`** owns everything about talking to Jira: `JiraClient` (a typed
`HttpClient` registered through `IHttpClientFactory`), request and response models,
`PersonalAccessTokenHandler`, the resilience pipeline, pagination, `JiraApiException` mapping,
and the capability probe. It references no MCP package and knows nothing about profiles,
credential stores, or tools.

**`JiraServerMcp`** owns everything else: the generic-host composition root, the MCP server
registration, tool classes, the CLI verbs, `ProfileStore` (a JSON file), `ICredentialStore` and
its per-platform implementations, grant parsing, and response rendering.

Dependency direction is one-way and enforced by the project reference. The composition root is
the only place that knows both halves.

**Configuration model.** Launch arguments select the profile and the grants. The profile file
supplies the base URL, an optional CA bundle path, and the cached capability probe. The
credential store supplies the token. Nothing else is configurable in the MVP, and options are
validated at startup with `ValidateOnStart` so a broken profile fails immediately with a
readable message rather than on the first tool call.

---

## 4. Distribution decision

| Option | Install | Upgrade | Credential store | MCP config | Verdict |
|---|---|---|---|---|---|
| .NET tool | `dotnet tool install -g` (needs .NET 10 SDK/runtime) | `dotnet tool update -g` | native | `"command": "jira-server-mcp"` | **primary** |
| Self-contained binary | download, unzip, mark executable | manual, or a self-update command | native | absolute path | **secondary** |
| `dnx jira-server-mcp` | none | implicit, and therefore surprising | native | works, not documented as the default | trial only |
| Docker image | `docker pull` | `docker pull` | **unreachable** | long `docker run` line | **rejected** (ADR-0004) |
| Hosted HTTP server | deploy it | deploy it | server-side, multi-user | URL | out of scope (ADR-0002) |

Against the stated priorities: security is a wash between tool and binary and a loss for Docker
(which forces credentials into environment variables or a bind mount); installation and
configuration favour the tool for a .NET team; client compatibility is identical; credential
isolation favours anything that runs as the user; upgradeability strongly favours the tool;
cross-platform support favours both; maintainability favours the tool, with the binary matrix as
a per-release cost that CI absorbs.

---

## 5. MCP transport decision

| | stdio | Streamable HTTP |
|---|---|---|
| Client support | universal | good, uneven in older clients |
| Credentials | stay on the machine | must be held server-side, per caller |
| Authorization | the OS user is the answer | needs a real authorization design |
| Attack surface | a pipe | a socket |
| Ops | none | a service to run |

**MVP ships stdio only** (ADR-0002). Tools are written against SDK abstractions with no transport
knowledge, so Streamable HTTP is later additive work whose real cost is the authorization design,
not the transport.

---

## 6. Authentication architecture

### The flow

```
jira-server-mcp profile add work --url https://jira.example.com
        │   URL validated: https, or a loopback address. No network call — see the correction below
        ▼
jira-server-mcp auth login work
        │   user creates a PAT in Jira: Profile → Personal Access Tokens → Create token
        │   prompt with echo off  (or a piped line on stdin, or the CI env var)
        │   validate: GET /rest/api/2/myself with Authorization: Bearer <token>
        │   show resolved display name + key, then store
        ▼
credential store: service "jira-server-mcp", account "<profile>"
        ▼
capability probe recorded on the profile: version, deploymentType, Software licensed?
        ▼
MCP client launches: jira-server-mcp serve --profile work --allow issues:write
```

No browser, no callback listener, no ephemeral port, no verifier, no CSRF surface — all of it
deleted by ADR-0001.

### Token lifecycle

A personal access token is valid until it expires or is revoked. Expiry is optional and set by
the user when creating it (administrators may cap it at 365 days, or forbid non-expiring tokens)
[fact]. There is no refresh, and no unattended re-authentication is possible or desirable: a new
token requires a human in Jira's UI.

Invalidation therefore surfaces as a `401`, and the response an agent gets must say what to do:

> Credentials for profile `work` are invalid or revoked. Run `jira-server-mcp auth login work`.

The same message is produced by `auth status`, which validates without printing anything secret.

### Credential storage

`ICredentialStore` with `Get`, `Set`, `Delete`, and `Describe`, selected at runtime and
overridable with `--credential-store`:

| Platform | Backend | Mechanism |
|---|---|---|
| macOS | Keychain | `security add-generic-password` / `find-generic-password` |
| Windows | Credential Manager | `CredWrite` / `CredRead` via P/Invoke, or DPAPI-encrypted file |
| Linux, session bus present | Secret Service | `secret-tool store` / `lookup` |
| Anywhere, opt-in | file | AES-GCM, `0600`, key from DPAPI on Windows / a `0600` machine key file elsewhere |

Shelling out to `security` and `secret-tool` follows Git Credential Manager's approach and buys
two things: no native interop to build per runtime identifier, and a trivially fakeable seam in
tests. Windows is the exception, and has to be: `cmdkey` writes and deletes a generic credential
but will never print one back, so a shelled-out Windows store could store a token and then fail to
retrieve it. Hence P/Invoke there [verified — `cmdkey` has no read verb]. The file store exists because headless Linux, WSL, and SSH sessions frequently have no
Secret Service, and hard-failing there would lock out real users; the README states plainly that
it protects against casual reading and not against a compromised user account.

### Multiple instances

Profiles are named. `profiles.json` holds one entry each — base URL, optional CA bundle path,
cached capability probe, timestamps — and no secrets. Credentials are separate entries in the OS
store keyed by profile name. A process serves exactly one profile (ADR-0005); the isolation
boundary is the process, which is the strongest one available without a sandbox.

### CLI surface

```
jira-server-mcp serve   --profile <name> [--allow <grants>] [--credential-store <choice>]
jira-server-mcp profile add <name> --url <url> [--ca-bundle <path>]
jira-server-mcp profile list                       # names and URLs — never secrets
jira-server-mcp profile refresh <name>             # re-run the capability probe
jira-server-mcp profile remove <name>              # also deletes the credential
jira-server-mcp auth login <name>                  # prompt, validate, store
jira-server-mcp auth status <name>                 # validate, print the resolved Jira user
jira-server-mcp auth logout <name>                 # delete the credential, keep the profile
```

Corrected against the implementation: `serve` takes no `--log-level` — logging is configured
through the host's own environment, and one more option earned nothing — and every verb that
touches a credential takes `--credential-store auto|native|file`. `profile list` prints names and
URLs; the version it would print lives on the capability probe, which `auth status` reports and
which a list of every profile would have to read for each one. And `profile add` validates the URL
without calling Jira: reachability is a different question from correctness, a laptop off the VPN
would fail to register a perfectly good instance, and `auth login` calls
`/rest/api/2/serverInfo` a moment later anyway.

`--token` as an argument does not exist, and passing it is an error rather than a warning:
arguments are visible to every process on the machine and land in shell history. The escape hatch
for containers and CI is `JIRA_SERVER_MCP__<PROFILE>__TOKEN`, documented as exactly that.

`System.CommandLine` 2.0.11 [fact — stable on nuget.org] provides the verbs. The no-echo prompt is
`Console.ReadKey(intercept: true)` and about thirty lines rather than `Spectre.Console`: one prompt
did not earn a dependency, and the output so far is plain lines, not tables.

---

## 7. MVP toolset

Sixteen tools. Read tools are always registered; agile tools are registered only when the
capability probe says Jira Software is licensed; write tools are registered only under a grant.
Every tool sets `readOnlyHint` / `destructiveHint` honestly, and every response that carries
Jira-authored free text delimits it as untrusted content (§12).

### Read — always registered

| Tool | Jira endpoints | Why it exists |
|---|---|---|
| `jira_whoami` | `GET /rest/api/2/myself` | The agent must know whose permissions it has before it reasons about "my issues". Cheap, and the first thing to check when something 403s. |
| `jira_search` | `GET`/`POST /rest/api/2/search` | JQL is the single highest-value operation in Jira. Defaults to 25 results, hard cap 100, projected fields, reports `total`. |
| `jira_get_issue` | `GET /rest/api/2/issue/{key}` plus per-expansion endpoints | The workhorse. `include: [comments, transitions, changelog, links, worklogs]` collapses five would-be tools into one, and hands the agent the transition list in the same call it will act on. |
| `jira_list_projects` | `GET /rest/api/2/project` | Orientation. Key, name, id, type — nothing else. |
| `jira_get_project` | `GET /rest/api/2/project/{key}`, `/statuses`, `/components`, `/versions` | Merged metadata: issue types, statuses, components, versions in one call, because an agent about to create an issue needs all four. |
| `jira_get_create_fields` | `GET /rest/api/2/issue/createmeta?projectKeys=&issuetypeNames=&expand=projects.issuetypes.fields` | Non-optional. Real 8.x projects have required custom fields, and without this every `jira_create_issue` fails on `customfield_10xxx is required`. |
| `jira_search_users` | `GET /rest/api/2/user/search?username=` | Assignment needs a username. Note Server keys users by `name`/`key`, not Cloud's `accountId` [fact]. |

### Agile — registered only where Jira Software is licensed

`jira_list_boards`, `jira_list_sprints`, `jira_get_sprint_issues`, `jira_get_backlog`, over
`/rest/agile/1.0/board`, `/board/{id}/sprint`, `/sprint/{id}/issue`, `/board/{id}/backlog`. They
answer "what am I meant to be working on", which is the question an agent is usually being asked
in disguise. Registering them unconditionally would advertise four tools that always 404 on a
Jira Core instance, and the model would try them.

### Write — registered per grant

| Tool | Grant | Endpoint |
|---|---|---|
| `jira_create_issue` | `issues:write` | `POST /rest/api/2/issue` |
| `jira_update_issue` | `issues:write` | `PUT /rest/api/2/issue/{key}` — fields *and* assignee, no separate assign tool |
| `jira_transition_issue` | `issues:write` | `POST /rest/api/2/issue/{key}/transitions` — accepts a transition *name*, resolves the id, optional comment and screen fields in the same call |
| `jira_add_comment` | `comments:write` | `POST /rest/api/2/issue/{key}/comment` |
| `jira_add_worklog` | `worklogs:write` | `POST /rest/api/2/issue/{key}/worklog` — `timeSpent` in Jira's own form (`"3h 30m"`), optional `started`, optional comment |

### Deliberately excluded from the MVP

Issue deletion (irreversible, and no agent should reach for it unprompted); comment edit and
delete; attachment upload and download (file paths crossing the MCP boundary is a path-traversal
surface that deserves its own design pass); issue linking; sprint mutation; watchers; votes;
bulk operations. Each is straightforward to add once the write path has real usage.

### Resources versus tools

The MVP exposes no MCP resources. A resource is right for stable, addressable, cacheable content;
everything here is either a query with parameters or content that changes under the agent's feet.
Project metadata is the one plausible candidate, and it is better served as a tool that can be
called with a project key than as a resource list that must be enumerated first.

---

## 8. Jira compatibility strategy

- **Target `/rest/api/2` throughout.** [fact] Jira Server 8.20.7 documents `api` and `auth` API
  names with current version `2`; there is no `/rest/api/3` on Server, and `/rest/api/latest` is
  a moving alias that would make behaviour depend on the deployment. Pin the number.
- **Platform versus Software.** Everything except boards, sprints, and backlog is platform API.
  The agile four require Jira Software and `/rest/agile/1.0`.
- **Capability probe, taken once.** `GET /rest/api/2/serverInfo` yields version and deployment
  type; `GET /rest/agile/1.0/board?maxResults=1` distinguishes licensed Jira Software (200) from
  its absence (404) [assumption — the exact status code on a Jira Core instance is worth
  confirming in Phase 0]. The result is stored on the profile with a 7-day TTL and refreshed by
  `profile refresh`. Startup does no network I/O.
- **Version-conditional behaviour lives in one place.** A `JiraCapabilities` record read from the
  probe, consulted at registration time. No `if (version >= …)` scattered through call sites. In
  the MVP surface there is currently exactly one such condition — Jira Software presence.
- **Honesty about reach.** CI runs 8.20.7. The README lists 8.14 as the floor and 8.20.7 as
  tested, and says everything else is expected-but-unverified. A second version in the nightly
  matrix (8.14.x or 9.x) is a cheap later addition if the harness proves stable.

---

## 9. Project structure

```
/
├── CONTEXT.md
├── README.md
├── LICENSE
├── .gitignore                      # generated by `dotnet new gitignore`, never hand-written
├── .editorconfig
├── global.json                     # pins the .NET 10 SDK and the MTP test runner
├── Directory.Build.props           # net10.0, nullable, analyzers, deterministic, Source Link
├── Directory.Packages.props        # central package management
├── JiraServerMcp.slnx
├── docs/
│   ├── adr/                        # 0001–0005
│   ├── design/architecture.md      # this file
│   └── agents/                     # existing agent conventions
├── src/
│   ├── JiraServerMcp.Jira/
│   │   ├── JiraClient.cs
│   │   ├── Authentication/PersonalAccessTokenHandler.cs
│   │   ├── Capabilities/            # serverInfo probe, JiraCapabilities
│   │   ├── Models/                  # issue, project, user, comment, worklog, board, sprint …
│   │   ├── Paging/
│   │   └── Errors/JiraApiException.cs
│   └── JiraServerMcp/
│       ├── Program.cs               # composition root, verb dispatch
│       ├── Cli/                     # serve, profile *, auth *
│       ├── Profiles/ProfileStore.cs
│       ├── Credentials/             # ICredentialStore + Keychain / CredMan / SecretService / File
│       ├── Tools/                   # one class per group: Issues, Search, Projects, Users, Agile
│       ├── Rendering/               # field projection, truncation, untrusted-content framing
│       └── Grants/
└── tests/
    ├── JiraServerMcp.Jira.Tests/            # unit + WireMock.Net
    ├── JiraServerMcp.Tests/                 # tools, CLI, credential store, profiles, rendering
    ├── JiraServerMcp.Protocol.Tests/        # in-process MCP client ↔ server, real protocol
    └── JiraServerMcp.JiraIntegration.Tests/ # real Jira 8.20.7, trait-gated
```

Dependency direction: `JiraServerMcp → JiraServerMcp.Jira`, and nothing else.

Names [fact — both NuGet ids are unclaimed]: package `JiraServerMcp`, command `jira-server-mcp`,
root namespace `JiraServerMcp`, repository `matthewrosse/jira-server-mcp`. No `Atlassian.*`
prefix: it reads as official and invites a trademark complaint.

### Code quality baseline

`net10.0`; `<Nullable>enable</Nullable>`; `<TreatWarningsAsErrors>` on when
`ContinuousIntegrationBuild` is true, so local iteration stays pleasant and CI stays strict;
`<EnforceCodeStyleInBuild>`; `.editorconfig` with analyzer severities; `NuGetAudit` at the
highest level with warnings as errors in CI; central package management; deterministic builds;
Source Link; `dotnet format --verify-no-changes` in CI; `IHttpClientFactory` for every outbound
call; options validated at startup; `CancellationToken` on every async method, no exceptions.

---

## 10. Testing strategy

Stack [facts]: **xUnit v3** (3.2.2 stable, built on Microsoft Testing Platform, tests compile to
self-contained executables), **Shouldly** 4.3.0, **NSubstitute** 6.2.0, **WireMock.Net** 2.14.0,
**Testcontainers** 4.13.0, coverage via `Microsoft.Testing.Extensions.CodeCoverage`.
FluentAssertions is avoided deliberately: v8 is commercially licensed at $129.95 per developer
per year, and v7 receives only critical fixes.

**Unit tests** — `JiraServerMcp.Jira.Tests`: model deserialization against captured 8.20.7
payloads, pagination, error mapping, the resilience pipeline's method discrimination, capability
probing. `JiraServerMcp.Tests`: grant parsing, profile store round-trips and file permissions,
credential store contract tests against a fake process runner, URL validation and the rejection
of `--token`, field projection, truncation, untrusted-content framing, and a redaction test that
captures a full log of an authenticated round-trip and asserts the token appears nowhere in it.

**HTTP integration tests** — WireMock.Net standing in for Jira, asserting on the wire: the
`Authorization: Bearer` header present and correct, no retry on `POST`, retry with backoff on
`GET` 503, `Retry-After` honoured, paging requests sequenced correctly, `4xx` bodies mapped to
the right exception type, oversized responses truncated rather than streamed into memory
unbounded.

**MCP protocol tests** — `JiraServerMcp.Protocol.Tests`: a real MCP client talking to a real
server instance over the protocol, with `JiraServerMcp.Jira`'s `HttpClient` pointed at WireMock.
This exercises client → server → tool → application → Jira client end to end without a Jira, and
covers what direct method invocation cannot: tool registration under different grants, schema
shape, `isError` results, annotations, and cancellation.

**Real Jira tests** — `JiraServerMcp.JiraIntegration.Tests`, trait-gated, against Jira 8.20.7:

- **Provisioning.** `docker compose` for humans, Testcontainers for CI, both using
  `postgres:13` [assumption — 8.20 supports PostgreSQL 9.6–13] and
  `atlassian/jira-software:8.20.7-jdk11` [fact — tag exists], with `ATL_JDBC_URL`,
  `ATL_JDBC_USER`, `ATL_JDBC_PASSWORD`, `ATL_DB_TYPE=postgres72`, and a `JIRA_HOME` volume.
- **Licensing.** Atlassian publishes 3-hour, 10-user timebomb licences for testing [fact]; the
  key is committed to `tests/fixtures/`, needs no account and no secret, and expires three hours
  after it is applied — long enough for any CI run. Whether a Data Center timebomb licence is
  accepted by 8.20.7 in single-node mode is **[unverified]** and is a Phase 0 item.
- **Setup.** The official image has no licence, admin-user, or setup-wizard-bypass environment
  variable [fact], so first-run setup is driven over HTTP against the wizard's endpoints. This is
  the most brittle part of the harness and is **[unverified]**.
- **Readiness.** Poll `GET /status` for `RUNNING`, then `GET /rest/api/2/serverInfo`. Budget
  three to five minutes for boot.
- **Seeding.** As administrator over basic authentication — a project, two users, a handful of
  issues with comments and worklogs, and a board. Basic auth is acceptable here because the
  harness is not the product.
- **Token.** The suite must authenticate exactly as a user does, so it needs a real PAT.
  `POST /rest/pat/latest/tokens` under basic auth is the first attempt; a UI form-post is the
  fallback; a pre-provisioned database dump is the last resort. **[unverified]**, Phase 0.
- **Coverage.** Representative reads (search, issue with every expansion, project metadata,
  createmeta, board and sprint) and writes (create, update, transition, comment, worklog), plus
  the end-to-end path through a real MCP client to the real Jira.
- **Cleanup.** Containers and volumes torn down per run; no state carried between runs.

---

## 11. CI/CD

**`ci.yml`** — pull requests and pushes. Restore, build with warnings as errors,
`dotnet format --verify-no-changes`, `NuGetAudit`, unit + WireMock + MCP protocol tests, coverage
summary. No Docker, no Jira. Target: under three minutes.

**`jira-integration.yml`** — nightly cron, `workflow_dispatch`, and a `run-jira-tests` pull
request label. Brings up Postgres and Jira 8.20.7, runs setup, applies the timebomb licence,
seeds fixtures, mints a PAT, runs the trait-gated suite, uploads Jira logs on failure. Never
gates a push: three to five minutes of Jira boot on every commit would be paid many times a day
for a signal that changes rarely.

**`release.yml`** — on tag. Pack the tool, publish to GitHub Packages, publish the six
self-contained binaries with checksums, generate build provenance attestation, draft release
notes. nuget.org publication is deliberately deferred until the tool surface stops moving; when
it happens, it uses trusted publishing over OIDC rather than a long-lived API key in secrets.

Classification, stated in the README so nobody guesses: unit, WireMock, and protocol tests are
fully automated and run everywhere; real-Jira tests are automated but slow and run on a schedule
or on request; nothing requires a secret or a purchased licence.

---

## 12. Security model

| # | Threat | Mitigation |
|---|---|---|
| 1 | Token stolen from disk | OS credential store by default; file fallback is opt-in, AES-GCM, `0600`, and documented as weaker. `profiles.json` never contains a secret. |
| 2 | Token leaked through logs | The token lives only in the auth handler and is written straight to the request header; no loggable object ever holds it. Logs carry method, URI, status, elapsed — never headers or bodies. A test asserts absence in a full log capture. |
| 3 | Token leaked through arguments | `--token` does not exist. Arguments are world-readable on most systems and land in shell history. |
| 4 | Token leaked through exceptions | Exception types carry status, endpoint, and Jira's error map; the request message is never attached. |
| 5 | Cross-profile leakage | Credentials keyed by profile in the OS store; one profile per process (ADR-0005). |
| 6 | SSRF via an agent-supplied URL | Base URL comes only from `profiles.json`. No tool takes a URL, a host, or a path segment that could escape the base. Request URIs are constructed, never concatenated from tool input. |
| 7 | MITM on an internal network | HTTPS required; only explicit `http://localhost` is exempt. `--ca-bundle` adds a private root as an explicit trust decision. **There is no `--insecure` switch** — it would be pasted into every teammate's config within a week. |
| 8 | Redirect to an attacker host | Automatic redirects disabled; a redirect response is an error, not a hop. |
| 9 | Path traversal via attachments | No attachment tools in the MVP. When they arrive, they get their own design pass and their own ADR. |
| 10 | Malicious or hostile Jira response | `System.Text.Json` with a depth limit and no polymorphic binding; no XML parsing anywhere; response bodies read under a size cap; unknown fields ignored. |
| 11 | Resource exhaustion from a huge result set | Page size capped at 100; issue bodies truncated with an explicit marker; total response budget enforced before serialization. |
| 12 | **Prompt injection from Jira content** | Jira content is attacker-controlled text arriving at a model. All Jira-authored free text is wrapped in explicit delimiters and marked as untrusted data that must not be treated as instructions. Content is **not** pattern-scanned or stripped: that mangles legitimate text, gives no guarantee, and an issue *about* prompt injection would be censored by it. The real bound on blast radius is that writes require a grant the user gave, not text an issue contains. |
| 13 | Destructive operations | No delete tools at all in the MVP. Writes are unregistered without a grant, so a forbidden operation is invisible rather than refused. `destructiveHint` set honestly so clients can confirm. |
| 14 | Confused deputy across instances | A tool cannot name an instance (ADR-0005). |
| 15 | Compromised dependency | Central package management, `NuGetAudit` failing the build, Dependabot, build provenance attestation on release artefacts. |
| 16 | Authorization boundary | The server has exactly the Jira permissions of the token's user. It cannot exceed them, and it does not try to model them — a `403` is surfaced as a permission problem with the operation named, and never retried. |

The honest limitation, stated in the README: this protects a developer's Jira token on a machine
the developer controls. It does not defend against malware already running as that user, and no
local credential store does.

---

## 13. README and documentation plan

`README.md`, in this order: what it is and why (one paragraph, the Cloud distinction up front);
supported Jira versions, split into tested and expected; requirements; installation (tool first,
binaries second); quick start — `profile add`, `auth login`, config snippet, first question to an
agent; creating a personal access token in Jira, with the exact UI path for 8.20; MCP client
configuration for Claude Code and VS Code / Copilot, both hand-verified, plus a generic stdio
snippet; managing several instances; the tool catalogue as a table with grants marked; the
security model, including the prompt-injection position and the file-store caveat; troubleshooting
(401, 403, 404-means-two-things, TLS with a private CA, no Secret Service on headless Linux,
Jira Software absent); development setup; running each test tier; running Jira 8.20.7 locally;
CI layout; packaging and publishing; compatibility notes; known limitations — and the limitations
section names what is *not* here (attachments, deletion, OAuth, HTTP transport) rather than
letting a reader discover the gaps.

Supporting documents: `CONTEXT.md` (glossary, already written), `docs/adr/0001`–`0005`,
`docs/design/architecture.md` (this file), and `tests/README.md` covering the Jira harness and
what to do when Jira's setup wizard changes shape.

---

## 14. Open questions

Everything here is Phase 0 work. Nothing downstream depends on the answers except the integration
harness, which is why Phase 0 runs before anything is built.

1. **Does a Data Center timebomb licence activate 8.20.7 in single-node mode?** If not, the
   harness needs a different licence source, and CI's story changes materially.
2. **Can Jira 8.20.7's setup wizard be driven over HTTP end to end?** Database configuration
   arrives by environment variable, but licence, administrator account, and mail setup are wizard
   steps. This is the single most brittle part of the plan.
3. **Can a PAT be created programmatically on 8.20.7** (`POST /rest/pat/latest/tokens`)? Atlassian
   documents only UI creation. Fallbacks: a UI form-post, or a pre-provisioned database dump.
4. **What does `/rest/agile/1.0/board` return on a Jira Core instance without Jira Software?**
   The capability probe's discrimination depends on it.
5. **How long does 8.20.7 take to reach `RUNNING` on a GitHub-hosted runner?** If it is far past
   five minutes, the nightly workflow needs a larger runner or a cached `JIRA_HOME` volume.
6. **Does `secret-tool` behave identically across GNOME Keyring and KWallet backends** for the
   store, lookup, and delete operations used here?

Decisions deferred rather than open: Streamable HTTP transport, OAuth 1.0a via LeanOAuth, basic
authentication for pre-8.14 instances, attachments, issue linking, and nuget.org publication.
Each is additive and none is blocked by the MVP's shape.

---

## 15. Implementation plan

Nine phases. Each states its goal, its deliverables, and the check that says it is done. Phases
are sequential; the dependency that matters is that Phase 0 precedes Phase 7's design and Phase 1
precedes everything else.

### Phase 0 — Resolve the unknowns (spike, throwaway code)

Answer questions 1–5 in §14 with a shell script and a running Jira. Boot
`atlassian/jira-software:8.20.7-jdk11` against `postgres:13`, apply a timebomb licence, drive the
wizard, mint a PAT, call `/rest/api/2/myself` and `/rest/agile/1.0/board`. Record every request
that worked in `tests/README.md`.

*Done when:* a documented sequence takes an empty Docker host to an authenticated
`GET /rest/api/2/myself` returning 200, with timings, or a written explanation of which step
cannot be automated and what the fallback is.

### Phase 1 — Repository scaffolding

`dotnet new gitignore` (generated, never hand-written), `global.json` pinning the .NET 10 SDK and
the MTP test runner, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`,
`JiraServerMcp.slnx`, the two source projects, the four test projects, and `ci.yml`.

*Done when:* `dotnet build` and `dotnet test` succeed locally and in CI on a pull request, with
warnings as errors and format verification passing.

### Phase 2 — Jira client core

`JiraClient` as a typed `HttpClient`; `PersonalAccessTokenHandler`; the custom resilience pipeline
(30-second total timeout; retry on GET and HEAD only, three attempts, exponential backoff with
jitter, on 408/429/5xx and `HttpRequestException`; `Retry-After` honoured; **no retry on POST,
PUT, or DELETE**; no circuit breaker); redirects disabled; TLS policy and the optional CA bundle;
`JiraApiException` mapping Jira's `errorMessages`/`errors` shape; paging over
`startAt`/`maxResults`/`total` and the agile API's `isLast`; `serverInfo` probe and
`JiraCapabilities`.

Not `AddStandardResilienceHandler` — it retries every HTTP method by default
(dotnet/extensions#5248, still open) [fact], which would silently create issues twice.

*Done when:* WireMock tests prove the header, the method discrimination, `Retry-After`, paging,
error mapping, and cancellation. Payload fixtures come from the Phase 0 instance.

### Phase 3 — Profiles, credentials, CLI

`ProfileStore` at `$XDG_CONFIG_HOME/jira-server-mcp/profiles.json` (macOS uses `~/.config` too —
developer CLIs live there, and .NET's `SpecialFolder.ApplicationData` returns
`~/Library/Application Support`, so the path is computed rather than asked for) [fact, verified on
this machine]. `ICredentialStore` with Keychain, Credential Manager, Secret Service, and file
implementations. All `profile` and `auth` verbs. URL validation at `profile add`; token validation
against `/rest/api/2/myself` before storing.

*Done when:* a contract test suite passes against every credential backend available on the CI
matrix; file permissions are asserted; `--token` is rejected; `auth login` on a bad token fails at
login rather than at first use.

### Phase 4 — MCP host and the walking skeleton

Generic host, MCP server over stdio, `serve` verb, grant parsing, logging to stderr with the
redaction test, and three tools: `jira_whoami`, `jira_search`, `jira_get_issue` (no expansions
yet). Field projection and truncation land here, because they are the reason `jira_get_issue` is
worth having.

*Done when:* `JiraServerMcp.Protocol.Tests` drives a real MCP client through all three tools
against WireMock, and the server has been attached to Claude Code by hand and asked a real
question.

### Phase 5 — The rest of the read surface

`jira_get_issue` expansions (comments, transitions, changelog, links, worklogs);
`jira_list_projects`; `jira_get_project`; `jira_get_create_fields`; `jira_search_users`;
capability-gated registration of `jira_list_boards`, `jira_list_sprints`, `jira_get_sprint_issues`,
`jira_get_backlog`.

*Done when:* protocol tests assert that the agile four are absent when the probe says Jira
Software is not licensed and present when it says otherwise, and that a default `jira_get_issue`
response stays inside its token budget for a realistically large issue.

### Phase 6 — Writes

`jira_create_issue`, `jira_update_issue`, `jira_transition_issue` (name-to-id resolution, optional
comment and screen fields), `jira_add_comment`, `jira_add_worklog`. Grant-conditional
registration, honest annotations, input validation, and untrusted-content framing applied to every
response that echoes Jira text.

*Done when:* protocol tests prove each write tool is absent without its grant and present with it,
that no write is ever retried, and that Jira's per-field validation errors reach the agent intact.

### Phase 7 — The real Jira harness

Compose file and Testcontainers fixture from Phase 0's findings; committed timebomb licence;
automated setup; readiness polling; fixture seeding; PAT minting; the trait-gated integration
suite covering representative reads and writes; the full end-to-end path from an MCP client to
real Jira; `jira-integration.yml`.

*Done when:* the nightly workflow goes green twice in a row from a cold cache, and a developer can
reproduce it locally with one command.

### Phase 8 — Documentation and release

`README.md` per §13, with the Claude Code and VS Code configurations verified by actually running
them; `tests/README.md`; `release.yml` producing the tool package and the six binaries with
checksums and attestation; a v0.1.0 tag published to GitHub Packages.

*Done when:* a teammate who has never seen the repository installs it, authenticates, and gets an
answer out of an agent, using only the README.

### Phase 9 — Post-MVP backlog, in likely order

Attachments (with a dedicated path-handling design); issue linking; comment editing; sprint
mutation; a second Jira version in the nightly matrix; Streamable HTTP with a real authorization
design; basic authentication for pre-8.14 instances; OAuth 1.0a via LeanOAuth; nuget.org
publication.
