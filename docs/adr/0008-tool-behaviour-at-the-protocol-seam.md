# ADR-0008: Tool behaviour is proven at the protocol seam

**Status:** Accepted (2026-08-17)

## Context

The repo carries four test projects and, until now, no recorded rule for choosing between them.
A review flagged the absence of a per-tool unit test file — nothing named `CreateIssueToolTests`,
`UpdateIssueToolTests`, and so on — as a coverage gap. It is a placement decision, not a gap, but
with the decision unrecorded a reviewer reading `tests/` cannot tell the difference, and the same
finding gets re-flagged the next time someone looks.

The audit that followed found the review's supporting claims false as stated. Only
`WhoamiToolTests.cs` exists under `tests/JiraServerMcp.Tests/` is false: the project holds
roughly 25 test files — `ToolCallTests`, `JiraToolErrorTests`, `ToolSurfaceTests`,
`ResponseBudgetTests`, `WorklogInputTests`, rendering, verbs, credential and profile stores. The
narrower, true observation is that only `WhoamiTool` has a test file named for a tool.
`TransitionIssueTool`'s Matching/Ambiguous/Unmatched handling is reachable only through a slow,
full-stack test is also false: it is covered in `TransitionCommentWorklogProtocolTests.cs` at
casing-insensitive match (:96), unmatched with what is available listed (:145), one name on two
transitions moving nothing (:168), and a failed transitions read never claiming a write was sent
(:197). And slow is false at the numbers: 102 protocol tests run in 1m 59s, about 1.2s each,
against a stated 3-minute PR-check budget. Every other write tool's branching is likewise proven
at the protocol seam: update refuses a no-op, clears a field distinctly from leaving it alone,
unassigns; comment refuses an empty body; worklog refuses an unreadable duration and start time;
create returns per-field rejection messages; each write is sent exactly once on timeout.

The one place the underlying claim held: `CreateIssueTool.Describe` branches on status code — a
`400` appends `jira_get_create_fields` advice, anything else must not — and only the `400` arm
was staged at the protocol seam. `503` and the timeout path were covered; `403` was not.

## Decision

Tests are placed by seam, not by cost or by risk:

1. **The wire seam** — what Jira is asked and what it answers — is proven in
   `JiraServerMcp.Jira.Tests`, against an HTTP double.
2. **The protocol seam** — what an agent observes when it calls a tool — is proven in
   `JiraServerMcp.Protocol.Tests`, over a real MCP client/server pair with `HttpClient` pointed
   at WireMock. Tool-specific branching (refusals, ambiguity, error advice, the shape of a
   success message) belongs here, because the protocol seam is where an agent actually meets the
   tool.
3. **Pure logic** — anything with no I/O: rendering, response budget, grant parsing,
   `WorklogInput`, `ToolCall`, `JiraToolError`, profile and credential stores — is tested in
   `JiraServerMcp.Tests`, where it lives.

Clause 3 is what makes the rule productive rather than restrictive: branch-heavy logic inside a
tool is a signal to extract a pure helper and test it directly, not a reason to reach below the
protocol seam with a handler double. The tool's use of that helper is then proven once at the
seam.

**Named carve-out.** `WhoamiToolTests` tests tool behaviour below the protocol seam,
deliberately. Transport-level failure modes — a socket that never answers, a caller that
cancels — cannot be staged at the protocol seam with WireMock; they need an `HttpMessageHandler`
that hangs. Both of its cases are that. The carve-out is for transport failure modes only, and is
not a precedent for tool unit tests generally.

The gap the audit did find is closed alongside this ADR: `WritesProtocolTests` gains a case
asserting that a create rejected with `403` describes the failure without the
`jira_get_create_fields` advice.

## Consequences

- A new test's placement is a lookup against the seam it proves, not a judgment call repeated
  per PR.
- Rejected: a handler-double unit test per tool. `TransitionIssueTool` and its siblings take a
  concrete `JiraClient`, so a "unit" test still stages an `HttpMessageHandler`. Same staging
  cost, strictly less fidelity — it proves the tool's branching without proving the tool is
  registered, schema-shaped, or correctly reporting `isError`.
- Rejected: an enforcement test over test filenames (asserting no `*ToolTests.cs` outside an
  allow-list). The rule's judgment call — is this pure logic or tool behaviour? — is exactly what
  a filename check cannot decide, and the allow-list becomes a second place to edit whenever a
  legitimate carve-out appears. The repo's existing convention tests (`ToolSurfaceTests`,
  `ToolCallConventionTests`, `ReadmeTests`) assert facts; this one would assert a taste.
- Cost is not a counter-argument: the protocol suite is 102 tests in 1m 59s against a stated
  3-minute PR-check budget.
