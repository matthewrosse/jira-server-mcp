# Test harness and the Jira it runs against

This document covers the real-Jira tier: how a Jira Server 8.20.7 is brought up from nothing,
how it is configured over HTTP without a human touching a browser, and how a personal access
token is minted so the suite authenticates exactly the way the product does.

Everything below was executed, not designed. The scripts under `scripts/phase0/` are the spike
that produced it — throwaway code, kept only because the durable harness is built from what it
proved. Where a claim here is unverified, it says so.

## Running it

```
./scripts/jira-up.sh
```

One command. It starts Jira 8.20.7 and Postgres from `tests/harness/docker-compose.yml`, then runs
the integration suite against them — which is what drives first-run setup, applies the licence,
seeds the fixtures and mints the personal access token. Tear it down, volumes included, with:

```
./scripts/jira-down.sh
```

**The licence expires three hours after it is applied.** Far longer than any run, and much shorter
than a working day: an instance left up overnight will not still work in the morning.

From a cold `docker compose` to sixteen green tests took **12m 01s** on an Apple Silicon host
running the amd64 image under emulation — the whole tier, provisioning included. That is a local
number on the slowest configuration this project has; it is not a CI budget. See
[Limitations](#limitations).

### The two provisioning paths

Setup, seeding and token minting are written once, in
`tests/JiraServerMcp.JiraIntegration.Tests/Harness/`, and only the way the containers start
differs:

| | Containers | Selected by |
|---|---|---|
| A developer | Compose, on a fixed port | `JIRA_HARNESS_BASE_URL` pointing at the running instance |
| CI | Testcontainers, on a random port | the variable being unset |

### What runs where

The Jira-backed tests carry `[Trait("Category", "JiraIntegration")]` and the workflows select on
it, not on the project:

- `ci.yml` runs every test project with `--filter-not-trait Category=JiraIntegration`. The harness
  project's own parser and readiness tests need no Docker and run there.
- `jira-integration.yml` runs `--filter-trait Category=JiraIntegration`, nightly, on
  `workflow_dispatch`, and on a pull request labelled `run-jira-tests`. Never on a push.

The assembly fixture that owns the Jira provisions lazily, so a run that selects only the
Docker-free tests starts no container.

### The spike's own scripts

Kept for reference; they are not the harness.

```
./scripts/phase0/run-all.sh          # empty Docker host to an authenticated 200
./scripts/phase0/07-secret-tool.sh   # the secret-tool contract, needs no Jira
```

## The sequence that worked

Measured on an Apple Silicon host under emulation (see [Limitations](#limitations)). Times are
cumulative from `docker compose up`.

| At | Step |
|----|------|
| 6s | Containers created; Postgres healthy |
| 95s | `GET /status` returns `{"state":"FIRST_RUN"}` |
| 199s | `GET /` finally returns `200` — the wizard is servable |
| ~200s | `GET /` redirects to `/secure/SetupApplicationProperties!default.jspa` |
| ~205s | Application properties posted |
| ~290s | Licence posted (this step alone takes 60–100s) |
| ~292s | Administrator account posted |
| ~355s | Mail notifications skipped; wizard lands on `/secure/WelcomeToJIRA.jspa` |
| ~365s | Personal access token minted; `GET /rest/api/2/myself` returns `200` |
| 378s | Fixtures seeded and payloads captured |

**`/status` is not a readiness signal on its own.** It flips to `FIRST_RUN` roughly a hundred
seconds before the web layer will serve anything; in between, `/` answers `302` and the page it
redirects to answers `503`. A harness that polls only `/status` races the wizard and fails on the
first request. Poll `/status` first, then poll `GET /` — following redirects — until it returns
`200`.

### Requests, verbatim

Every one of these succeeded. The wizard steps are form posts against a session cookie jar; each
needs the `atl_token` cross-site request forgery token **taken from the `atlassian.xsrf.token`
cookie**, not the value embedded in the form, and a `Referer` header pointing at the instance.
Without both, the post returns `403`.

```
GET  /                                    -> 200, redirects to SetupApplicationProperties!default.jspa
POST /secure/SetupApplicationProperties.jspa   atl_token, baseURL, mode, nextStep, title
POST /secure/SetupLicense.jspa                 atl_token, setupLicenseKey
POST /secure/SetupAdminAccount.jspa            atl_token, confirm, email, fullname, password, username
POST /secure/SetupMailNotifications.jspa       atl_token, noemail=true, and the 17 other fields the form ships
```

Then, as the administrator over basic authentication:

```
POST /rest/pat/latest/tokens
     Content-Type: application/json
     X-Atlassian-Token: no-check
     {"name":"phase0-spike","expirationDuration":1}
  -> 201 {"id":1,"name":"…","createdAt":"…","expiringAt":"…","rawToken":"…"}
```

And finally, as the product authenticates:

```
GET /rest/api/2/myself
    Authorization: Bearer <rawToken>
 -> 200
```

## What the spike established

### 1. A Data Center timebomb licence does activate 8.20.7 in single-node mode — yes

`tests/fixtures/jira-dc-timebomb-3h.license` is Atlassian's published *10 user Jira Software Data
Center, 3 hours* key, taken from the
[timebomb licences page](https://developer.atlassian.com/platform/marketplace/timebomb-licenses-for-testing-server-apps/).
It needs no account, no purchase, and no repository secret. Applied to a stock
`atlassian/jira-software:8.20.7-jdk11`, it activates cleanly and yields ten seats.

Worth knowing for the capability probe: even under a Data Center licence, `serverInfo` reports
`"deploymentType": "Server"` on a single node. Deployment type does not distinguish the licence.

`tests/fixtures/jira-starter-timebomb-3h.license` is the *10 user starter non-eval host product*
key from the same page, used to license the Jira Core instance in finding 4.

### 2. The setup wizard can be driven over HTTP end to end — yes, in four posts

The four posts listed above complete it. Two traps cost real time:

- **The database step must be skipped by configuration, not by posting through it.** See
  finding 7.
- **The licence page carries two forms posting to `SetupLicense.jspa`.** The first is a stub
  holding nothing but `atl_token`; posting it returns `500`. The real form — the one carrying
  `setupLicenseKey` — is the second. A driver that takes the first matching form breaks here.

The wizard terminates at `/secure/WelcomeToJIRA.jspa`, not at the dashboard; the dashboard only
follows once a human clicks through.

### 3. A personal access token can be minted programmatically — yes

`POST /rest/pat/latest/tokens` under administrator basic authentication returns `201` with the
token in `rawToken`. Atlassian documents only creation through the user interface, but the
endpoint is there and works on 8.20.7. **Neither fallback is needed**: no user-interface form
post, no pre-provisioned database dump. `expirationDuration` is in days.

The raw token is returned once and never again, exactly as in the interface.

### 4. The software API on an instance without Jira Software — `404`, with an HTML body

Compared side by side against `atlassian/jira-core:8.20.7-jdk11`:

| Request | Jira Software | Jira Core |
|---------|---------------|-----------|
| `GET /rest/agile/1.0/board?maxResults=1` | `200`, JSON page envelope | `404`, **HTML error page** |
| `GET /rest/api/2/applicationrole` | `200`, `jira-software` with `"defined": true` | `200`, `jira-software` with `"defined": false` |
| `GET /rest/api/2/serverInfo` | `200`, `deploymentType: Server` | `200`, `deploymentType: Server` |

Two consequences for the capability probe:

- **The `404` body is HTML, not JSON.** A probe that parses the body before checking the status
  will throw a deserialization error rather than record an absence. Discriminate on the status
  code alone.
- **`applicationrole` is the better signal.** It answers `200` with JSON on both kinds of
  instance, and `"defined"` carries the licence state directly — no `404` handling, no HTML, no
  error path on what the glossary calls a normal condition. The architecture currently specifies
  the board probe; this is worth revisiting when the probe is built.

### 5. Time to ready — measured locally only, not on a hosted runner

199s to a servable wizard and 378s for the whole sequence, on an Apple Silicon host running the
image under emulation. **This does not answer the question that was asked.** The question is
about a GitHub-hosted runner, which is amd64 and runs the image natively; no hosted-runner
measurement was taken. See [Limitations](#limitations).

What the local numbers do establish is that the three-to-five minute boot budget in the design
document is optimistic about *readiness* if readiness means the wizard is servable, and that the
licence post is itself a 60–100s operation nobody had budgeted for.

### 6. `secret-tool` across keyring backends — they do not behave identically

Run in throwaway Debian 12 containers, because neither backend can be installed on the macOS host
the spike ran from.

**GNOME Keyring** provides `org.freedesktop.secrets` and satisfies the whole contract:

| Operation | Exit code | Output |
|-----------|-----------|--------|
| `store` | 0 | none |
| `lookup` (present) | 0 | the value, byte-exact |
| `lookup` (absent) | 1 | empty |
| `clear` | 0 | none |
| `lookup` after `clear` | 1 | empty |

**KWallet does not provide `org.freedesktop.secrets` at all** as packaged on Debian 12. The
KWallet packages install `/usr/bin/kwalletd5` and register `org.kde.kwalletd5`, and no file in
`/usr/share/dbus-1/services/` offers the Secret Service name. Every `secret-tool` operation fails
identically:

```
secret-tool: The name org.freedesktop.secrets was not provided by any .service files
```

The design consequence is concrete: **exit code 1 means both "no such secret" and "no Secret
Service on this machine".** The credential store adapter cannot tell them apart from the exit
code and must read standard error to distinguish a missing credential from a missing backend —
the first is normal, the second should send the user to the encrypted file store. The encrypted
file store is therefore needed on KDE desktops, not only on headless Linux and WSL as the design
document assumes.

*Caveat, stated because it matters:* `kwalletd5` aborts inside a container without a graphical
session, so a fully running KWallet was not exercised. The finding rests on the absence of the
D-Bus service file, which is what `secret-tool` resolves against and which is independent of
whether the daemon is running. A KDE Plasma desktop with `kwallet-secret-service` or an
equivalent bridge installed may behave differently; that remains unverified.

### 7. `ATL_DB_DRIVER` is required, and its absence is silent

Not one of the six questions, but it cost the most time, so it is written down.

The design document lists `ATL_JDBC_URL`, `ATL_JDBC_USER`, `ATL_JDBC_PASSWORD` and `ATL_DB_TYPE`
as the database configuration. That set is incomplete. The image renders `dbconfig.xml` from
`/opt/atlassian/etc/dbconfig.xml.j2`, and the template writes `<driver-class>` straight from
`ATL_DB_DRIVER` **with no default**. Set the other four and leave this one unset and the file is
generated, is read at boot without complaint, and Jira then logs:

```
[c.a.j.config.database.DatabaseConfigurationManagerImpl] The database is not yet configured.
```

— and presents the wizard's database step as though nothing had been configured at all. There is
no error. `ATL_DB_DRIVER: org.postgresql.Driver` fixes it and the database step disappears.

## Fixtures

`tests/fixtures/payloads/8.20.7/` holds 21 payloads captured from the canonical version over a
personal access token — the same authentication path the product uses, not the administrator
basic-auth path the harness seeds with. `index.json` maps each file to the request that produced
it.

They cover the current user, server info, JQL search, an issue both bare and expanded with
`changelog,renderedFields,transitions`, transitions, comments, worklogs, the project list and
detail, statuses, components, versions, `createmeta`, user search, the field list, and the board,
sprint, backlog and board-issue endpoints of the software API.

One number worth carrying into Phase 2: **a single trivial issue with one comment and one worklog
deserializes from 12 KB of JSON, and 20 KB with expansions.** The default field projection is not
a nicety.

The seeded issue descriptions deliberately contain wiki markup and a line reading like an
instruction, so the untrusted-content framing has something real to wrap.

## Refreshing the testing licence

`tests/fixtures/jira-dc-timebomb-3h.license` is a key Atlassian publishes for exactly this
purpose, and it is committed because it needs no account, no purchase and no repository secret.
Two different clocks matter, and only one of them is the three hours:

- **The three hours** are the licence's life *once applied to an instance*. Every run applies it
  afresh, so this never needs refreshing — it only means an instance left up overnight is dead in
  the morning. Tear it down and bring it up again.
- **The published key itself is rotated by Atlassian**, and the old one then stops activating.
  That is what needs refreshing, and it looks like the harness failing at the licence step with
  the page reporting an invalid or expired licence key.

To refresh it:

1. Open the
   [timebomb licences page](https://developer.atlassian.com/platform/marketplace/timebomb-licenses-for-testing-server-apps/)
   and take the **10 user Jira Software Data Center, 3 hours** key.
2. Replace the whole contents of `tests/fixtures/jira-dc-timebomb-3h.license` with it. Line
   wrapping does not matter: the harness strips every whitespace character before posting, because
   the published key is wrapped for display and Jira wants it unwrapped.
3. Verify it end to end with `./scripts/jira-up.sh`. A licence that does not activate fails at the
   wizard's licence step, so a green run is proof.

`tests/fixtures/jira-starter-timebomb-3h.license` is the *10 user starter non-eval host product*
key from the same page. Nothing in the durable harness reads it — it licensed the Jira Core
instance in finding 4 — and it refreshes the same way.

Do not replace either with a licence tied to an account. A repository secret would put the
Jira-backed suite out of reach of a fork and of anyone reproducing a failure locally.

## When the wizard changes shape

Two drivers exist, and they behave the same way for the same reason. `JiraSetupWizard` in
`tests/JiraServerMcp.JiraIntegration.Tests/Harness/` is the durable one, ported from the spike's
`scripts/phase0/02-setup.py`; what follows describes both, and the C# specifics are noted where
they differ.

`scripts/phase0/02-setup.py` does not hard-code the sequence. It fetches whatever page Jira is
showing, parses the form off it, fills the fields it recognises from `FIELD_VALUES`, and posts —
so a reordered wizard, or one with a step added, is handled without a change. What breaks it is a
*renamed field* or a *new required field it has no answer for*.

When it breaks:

1. It saves the offending page to `scripts/phase0/captured/setup-step-N-*.html` and stops. Open
   that file first — the failure is almost always visible in the form.
2. Compare the field names in the log line `step N: form -> … fields=[…]` against `FIELD_VALUES`.
   A field in the form and not in the table gets posted with whatever value Jira shipped, which
   is usually empty.
3. If a step re-renders itself rather than advancing, the post was rejected. Check the
   `atl_token` handling and the `Referer` header before suspecting the field values.
4. If the page has several forms with the same action, confirm the driver is picking the one with
   the most fields — the licence step depends on this.

`JiraSetupWizard` works the same way, with the answers in its `Fill` method rather than in
`FIELD_VALUES`, and it does not write the page to disk — it throws instead, carrying whatever the
page reported in its own error markup, so the message names the rejected field rather than a step
number. Two of its behaviours exist because the alternatives failed:

- A page that is neither a wizard step nor a configured instance is a failure **on the first
  fetch** and a finished wizard on any later one. Jira does not always terminate on
  `WelcomeToJIRA.jspa`; it can land on an ordinary page, and a driver that keeps hunting for "some
  form" finds the site's quick search and posts that to itself until it runs out of steps.
- Fields the answers do not name are carried back exactly as Jira sent them. The mail step ships
  nineteen fields, and one posted without them re-renders itself rather than advancing.

Re-running against an instance somebody has already set up is not an error: the first fetch lands
on a terminal page and the wizard returns.

## Limitations

- **No hosted-runner timing was taken.** Question 5 is answered only for a local Apple Silicon
  host. The nightly workflow's budget should not be set from these numbers.
- **The image is amd64-only.** `atlassian/jira-software:8.20.7-jdk11` publishes a single-arch
  manifest with no arm64 variant, so Apple Silicon runs it under emulation and every timing here
  is pessimistic relative to CI. The Compose file pins `platform: linux/amd64` to make this
  explicit rather than accidental.
- **KWallet was not exercised with a running daemon.** See the caveat in finding 6.
- **The licence expires three hours after it is applied.** Long enough for any CI run, and not
  long enough to leave an instance up overnight and expect it to still work.
- **`scripts/phase0/captured/` is not committed.** It holds a session cookie jar and a live
  personal access token alongside the traces. The findings it produced are in this document and
  the payloads it captured are under `tests/fixtures/payloads/`.
