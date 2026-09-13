# ADR-0004: Ship as a .NET tool and as self-contained binaries; not as a Docker image

**Status:** Accepted (2026-08-13), amended (2026-09-13)

## Context

An MCP server has to be something a user can name in `mcp.json` as a `command`. The candidates
were a .NET tool on a NuGet feed, self-contained single-file binaries per runtime identifier, and
a Docker image — the last being how a large share of published MCP servers ship.

Docker fails on this project's specifics rather than in general. The server reads its credential
from the host operating system's credential store, which a container cannot reach; the profile
configuration lives in the host's config directory, which has to be bind-mounted; and every user's
MCP configuration grows a `docker run -v … -e …` incantation whose failure modes are Docker's, not
ours. The isolation a container would buy is worth little here, because the process already holds
exactly one user's Jira token and nothing else.

## Decision

Primary: a .NET tool published to a NuGet feed, invoked as `jira-server-mcp`, upgraded with
`dotnet tool update`. Secondary: self-contained single-file binaries for `win-x64`, `win-arm64`,
`osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`, attached to GitHub releases for users with no
.NET SDK. Docker is used only to run the Jira instance the integration tests exercise.

## Consequences

- The MCP configuration a user copies is one line with a command name and arguments, which is the
  format every client documents.
- Two artefacts must be produced and smoke-tested by the release workflow, and version numbers
  must agree between them.
- `dnx jira-server-mcp` works for free as a zero-install trial path, but the documented
  configuration pins an installed tool: resolving "latest" on every agent launch is a surprise
  nobody wants inside an editor.

## Amendment (2026-09-13): the binaries are the only published artefact, for now

The first tag, v0.1.0, ran the release workflow end to end, and it pushed the tool package to
GitHub Packages. That feed authenticates every read, public packages included, so installing from
it takes a classic personal access token with `read:packages` and a `dotnet nuget add source` step
before `dotnet tool install` does anything. The binaries on the release page need neither. A
primary artefact that is harder to install than the secondary one is primary in name only, and
nuget.org, the feed that would make the tool the easy path, is still deferred until the tool
surface stops moving.

**Until nuget.org publication is taken up, the self-contained binaries attached to a GitHub release
are the only thing a release publishes.** The workflow still packs the tool and installs and starts
it on every tag, because that job is where the tests run and because the tool must be ready to
publish when this amendment is reversed; it pushes it nowhere. The v0.1.0 package was deleted from
GitHub Packages. Installing the tool means packing it from a clone.

When nuget.org publication happens, it restores the Decision above as written, over trusted
publishing rather than a stored API key, and GitHub Packages is not reinstated beside it.
