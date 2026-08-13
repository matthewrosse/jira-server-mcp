# ADR-0002: stdio is the only transport

**Status:** Accepted (2026-08-13)

## Context

The MCP C# SDK supports stdio and Streamable HTTP. Streamable HTTP is what a shared, remotely
hosted MCP server uses, and the SDK's stateless-first model makes hosting one straightforward.

But a remote server has to answer a question a local one never asks: who is the caller, and
whose Jira credentials should this request use? Answering it means an authorization layer, token
issuance, per-caller credential mapping, and a listening socket to defend — an
OAuth-2-resource-server project bolted onto a Jira client. None of that serves the actual use
case, which is a developer's own agent talking to Jira as that developer.

## Decision

stdio only. The server is launched as a subprocess by the MCP client, reads one profile's
credential from the local credential store, and acts as exactly one Jira user.

## Consequences

- Credentials never leave the machine, no port is opened, and the process lifetime is the
  client's problem rather than a service to operate.
- Tools are written against the MCP abstractions with no transport knowledge, so adding
  Streamable HTTP later is additive. Nothing in the tool layer needs to change; the
  authorization question does need answering first.
- stdout belongs to the protocol. All diagnostics go to stderr, and a stray `Console.WriteLine`
  is a protocol corruption bug, not a cosmetic one.
