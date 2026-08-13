# ADR-0005: One profile per process, chosen by launch arguments

**Status:** Accepted (2026-08-13)

## Context

A single installation must serve several Jira Servers. The natural-looking design gives every
tool a `profile` argument, so one running server can reach all of them.

That design puts the choice of which Jira gets written to inside the model's context window,
where it competes with everything else the agent is juggling. The failure it invites — a write
landing on production because the wrong string was passed — is silent, plausible, and
unrecoverable. It also adds a required argument to every tool schema, permanently, in a design
where response size and schema noise are already the scarce resource.

## Decision

The profile is selected once, by the process's launch arguments (`--profile prod`), and is
invisible to tools. Talking to two Jiras means two entries in the MCP client configuration.
Write grants are given the same way (`--allow issues:write`), so what a given client may do is
visible in the file the user already reads to configure it.

## Consequences

- A tool call cannot address the wrong instance, because it cannot address an instance at all.
- Different clients can hold different powers over the same profile — read-only in one editor,
  write-enabled in another — with no shared configuration to reconcile.
- Several server processes may run at once. Nothing is shared between them but the credential
  store and the profile file, both read-mostly, so this costs a little memory and no coordination.
- An agent cannot discover other profiles, which is the point. `jira-server-mcp profile list` is
  for the human.
