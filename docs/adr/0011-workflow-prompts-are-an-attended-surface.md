# ADR-0011: Workflow prompts are an attended surface

**Status:** Accepted (2026-08-18)

## Context

The 2026-08-18 architecture review framed prompts as "what the server needs before a coding agent
can run a whole unit of work unattended". Grilling the issue rejected that framing on a fact about
the protocol.

An MCP prompt is user-initiated. A client surfaces it as something a human picks — a slash command
they type — and there is no path by which a model in mid-loop fetches one for itself. So a prompt
removes no step from an unattended run. What it removes is typing at the start of an attended one:
the multi-step procedure that today lives in a CLAUDE.md paragraph, a client-side slash command, or
a prompt retyped in every repository that talks to this server, and that has to be retyped again
whenever a tool is renamed.

That is worth having on its own terms. It is not what the review thought it was asking for.

## Decision

Ship `implement_issue` as a **human kickoff surface**, and say so plainly in the README rather than
letting a reader infer that prompts do something for an autonomous agent.

The message is static text, one `user` message, interpolating an optional `key` argument. It reads
nothing — not Jira, not the profile, not the grant set — so there is no fetch that can go stale, no
failure mode at prompt-fetch time, and no untrusted content in the message. A caller-supplied key
is the caller's own text, not Jira's.

MCP prompt messages carry only `user` and `assistant` roles. A synthetic `assistant` turn would
put words in the model's mouth that it never said, so the procedure is one `user` message and the
model's first turn is its own.

The prompt never names a status. Status vocabulary is per-team and this server does not know it, so
the procedure tells the agent to list the issue's own transitions and take the one that means work
started. Shipping "move it to In Progress" would ship one team's workflow to everyone, and would be
wrong on the first instance that calls it something else.

The gate is **derived, never re-declared**. A prompt row names the tools its procedure calls, and
`PromptSurface` registers the prompt only where every one of those tools survived
`ToolSurface`'s own gate. A `RequiredGrant` on a prompt row would be a second copy of the tools'
gate, free to drift: a tool moved to another grant would leave its prompt registered against a
client that can no longer follow it. Because `implement_issue` requires `jira_get_issues`,
`jira_transition_issue` and `jira_add_comment`, the grant set it runs under is constant — so the
message needs no sentence about which writes are permitted and no licence branch.

`ToolSurface` is unchanged. A second table reads it, rather than one table growing a column that
means something different per row.

## Rejected: an agent-callable briefing tool

The thing the review actually described — `jira_workflow_brief`, a tool the agent calls for itself
mid-loop — is deliberately not built. It would work, because tools *are* agent-callable, and it is
the honest answer to "help an unattended run".

It is not built because nobody has asked for it. Its cost is not the code: it is one more tool in
every client's tool list, consuming context on every single call, to deliver text that a client
which wanted it could put in its own system prompt once. If a real unattended workflow turns out to
need it, this is the ADR to amend.

## Consequences

- `implement_issue` appears in a client's `prompts/list` only under a grant set carrying
  `issues:write` and `comments:write`. A read-only client sees no prompts at all, which is correct:
  it cannot follow this one.
- The README documents prompts in their own section, separate from the tool catalogue, because the
  two are surfaced differently and reached differently.
- `standup` and `triage`, sketched in the original issue, are cut. Both encode one team's policy,
  and `standup` is served by tools that already exist.
- Adding a prompt means adding a row and its required tools. Nothing about grants is restated.
