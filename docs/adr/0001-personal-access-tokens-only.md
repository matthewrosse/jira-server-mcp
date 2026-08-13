# ADR-0001: Personal access tokens are the only credential

**Status:** Accepted (2026-08-13)

## Context

Jira Server offers two credentials a third-party tool can realistically use. OAuth 1.0a with
RSA-SHA1 signing is the historically blessed one, and personal access tokens — a bearer token in
an `Authorization` header — have existed since Jira Core and Jira Software 8.14, in Server as
well as Data Center.

OAuth 1.0a on Jira Server requires a Jira administrator to create an Application Link incoming
link holding a consumer key and an RSA public key. That makes the interesting question not
"which protocol is better" but "who holds the private key". Either one application link serves a
whole team and its private key is distributed to every member — a shared secret whose leak lets
the holder impersonate the consumer against every user who ever authorized it — or an
administrator creates one application link per user, forever. Neither is acceptable for a tool
meant to be installed in a minute.

A personal access token is issued by the user to themselves, needs no administrator, is revocable
from the same screen that created it, and reduces the entire authentication design to one header.

## Decision

Personal access tokens are the only credential. The supported floor is Jira 8.14. Authentication
sits behind a single interface that produces an HTTP message handler, so basic authentication or
OAuth 1.0a can be added later without reshaping anything above it.

## Consequences

- Jira instances below 8.14 are unsupported, and the README says so rather than implying wider
  reach. The canonical test target, 8.20.7, is also what the primary users run.
- No browser, no local callback listener, no ephemeral port, no RSA keypair, no CSRF surface, no
  application-link provisioning in the integration harness. A large part of the originally
  imagined design disappears, along with its threat model.
- LeanOAuth is not a dependency of this project, even though LeanOAuth's own ADR-0003 names an
  MCP server talking to Jira 8 as the first consumer of its v2 design. That consumer is now
  hypothetical; if OAuth 1.0a is ever added here, LeanOAuth remains the intended implementation.
- The token is a bearer secret with no proof-of-possession, so transport security and storage
  carry the full weight that a signature would otherwise share: TLS is mandatory, and the token
  must never reach a log, an argument vector, or an error message.
