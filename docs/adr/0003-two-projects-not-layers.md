# ADR-0003: Two source projects, not architectural layers

**Status:** Accepted (2026-08-13)

## Context

The obvious shape for a project described as "MCP server over a REST client" is the layered one:
Domain, Application, Infrastructure, Mcp, Cli. It is what most .NET reference architectures show,
and it would be uncontroversial to adopt here.

It would also be mostly assemblies. There is one genuine boundary in this system: the Jira client
must be testable and reusable without any MCP concept present, so that its behaviour — signing,
paging, error mapping, resilience — can be exercised against an HTTP double alone. Every other
proposed boundary separates code that changes together, called from one composition root, by one
process, for one user.

## Decision

Two source projects.

- `JiraServerMcp.Jira` — typed HTTP client, request/response models, the authentication handler,
  pagination, error mapping, capability probing. No reference to any MCP package.
- `JiraServerMcp` — MCP host, tools, CLI verbs, profile store, credential store, composition.

One dependency edge, pointing from the host to the client.

## Consequences

- The client can be published as its own package later without untangling anything, and it is
  independently useful to someone who wants a Jira 8 client and no MCP.
- Tool classes are thin: they translate arguments, call the client, and shape output. The rule
  "no raw HTTP in a tool class" is enforced by the project reference direction, not by review.
- If a third project ever earns its place, it will be because something concrete demanded it —
  not because a diagram has five boxes.
