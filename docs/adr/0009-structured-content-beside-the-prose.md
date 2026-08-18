# ADR-0009: Structured content beside the prose, carrying identifiers only

**Status:** Accepted (2026-08-18), amended (2026-08-18)

## Context

Every tool answers with rendered text and nothing else. That is right for a model reading the
answer and useless for a workflow that wants to branch on it. An agent asking "is this issue still
open?" or "which of these twenty keys failed?" re-reads English prose it has already paid for, and
any change to a rendering module's layout silently breaks whatever was scraping it.

MCP has a place for the machine-shaped half — `structuredContent` on a tool result, with an
optional `outputSchema` on the tool — and the SDK exposes both. What the protocol does not decide
is what a server should put there, and for this server two of its own commitments constrain the
answer.

The first is the untrusted content envelope. Free text authored inside Jira reaches a model, so it
is delimited and marked as data rather than instructions. JSON has no place to put "the lines
between these markers": a per-field marker convention would be one this server invented and no
client understands.

The second is the response budget. The whole project exists to control what an answer costs an
agent. The MCP specification's backward-compatibility clause asks a tool returning structured
content to *also* return the same data serialized as JSON in a text block, for clients that ignore
`structuredContent`. Honouring it here would mean every fact is paid for twice, and the text block
is where the prose already lives.

## Decision

Tool results carry structured content beside the prose, under four rules.

**1. The structured half is a contract; the prose is not.**

Field names and types may be added, never removed and never retyped. Exact-equality tests over the
serialized structure are what make that enforceable rather than aspirational. The prose keeps its
current freedom to be reworded, because nothing is promised to depend on it — that freedom is the
reason the structured half exists.

**2. It carries identifiers and the values Jira enumerates. Never issue prose.**

Issue keys and ids, status ids and status names, issue type names, transition ids, usernames,
paging positions, per-key outcomes, counts. Not summaries, not descriptions, not comment bodies,
not display names — those stay inside the delimited region and nowhere else.

A status name is admin-authored and so is untrusted in provenance, but it is the field the
motivating question turns on, and an agent restricted to status ids would need a second lookup to
answer "is this still open?". The line is therefore drawn at *prose*, not at *provenance*: a value
Jira enumerates from a fixed set is carried, a value a human typed into a text box is not. The
whole structured half is declared untrusted once, in the README, rather than per field.

**3. Structure is present on every result, success and failure alike.**

Every result carries an `outcome`: `ok`, `jira_api` with the status code, `unreachable`,
`timed_out`, or `refused` for a call the tool rejected before reaching Jira. "Was this a
permissions problem or a dead network?" is the branch an agent most needs and can least reliably
recover from a sentence, and "structure is always present" is a far easier promise to document
than "structure is present sometimes".

The bulk read keeps one shape whether or not `isError` is set. Its per-key failures live in a
`failures` list, because a partial success is not an error and a caller must not see the shape
appear and vanish with the number of bad keys.

**4. Both halves come off one traversal.**

Rendering modules return a text-plus-structure pair rather than a string, and `ToolCall` sets both
on the result. A second module walking the same model to build the structure would reintroduce
exactly the drift this decision exists to prevent. Where a page is cut by the response budget, the
structured half is cut with it and carries the resume position: two halves of one response that
disagree on their row count would be that same drift wearing a different hat.

Output schemas are declared per tool. Only fields the server itself always produces are marked
required; anything sourced from Jira's response is optional, because Jira Server versions differ in
what they return and a missing field must not turn a good answer into a protocol error.

## Consequences

- **The specification's backward-compatibility clause is deliberately not followed.** No tool
  echoes its structured content as serialized JSON in a text block. The clause is a SHOULD whose
  stated purpose is legacy clients; honouring it would double the context cost of every answer,
  against a project whose premise is that the prose is the readable half. A client that ignores
  `structuredContent` still gets a complete, readable answer — it simply gets it as prose.
- `JiraIssueDetail` gains typed status and issue type properties rather than leaving them in the
  open field dictionary. The alternative — teaching a rendering module Jira's field ids — puts
  Jira's vocabulary in the wrong project. The record already carves out the sections whose shape
  Jira fixes; these are more of the same carve-out, not a new rule.
- The structured half is bounded by construction: rule 2 admits only short values, and rule 4 makes
  it inherit the prose's budget cut. A test pins the worst-case serialized size, because rule 1
  guarantees fields will be added and "bounded by construction" stops being true the first time
  someone adds a description.
- A protocol-seam test asserts every registered tool's result carries structure, on success and on
  failure. Under ADR-0008 this is a fact test, not a taste test: it asserts the exact promise the
  README makes.
- Rejected: carrying Jira's raw field dictionary as the structured half. It is cheap to build and
  it hands the agent the payload the field projection exists to prevent, prose and all.
- Rejected: leaving the structured half undocumented and best-effort. It would move where the
  scraping happens without making anything safe to depend on, which is the problem restated rather
  than solved.

## Amendment (2026-08-18): selection labels, and paging fields that were actually given

From the grilling of #78–#81, which took the four renderers ADR-0009's first pass left text-only —
boards, sprints, projects and users, and the create screen — and found rule 2 too narrow to serve
them and one paging question unanswered. Both are recorded here rather than as a new ADR: neither
changes the decision, and a reader of rule 2 who did not find these beside it would apply the old
line and get the wrong answer.

**Rule 2 admits a selection label.** An admin-typed name for a row of an enumerable set whose
identifier is opaque may be carried. A board id names nothing, `customfield_10010` names nothing,
and a sprint id names nothing; the name is the only basis an agent has for choosing between rows or
for knowing what an identifier it must send verbatim actually is. Withholding it would leave the
structured half unable to answer the question its rows exist to answer, and would send the agent
back to the prose to pick a row — which is the loop the whole decision exists to close.

This does **not** admit display names or issue prose. A person's display name identifies nothing a
follow-up call can use (the username does), and a summary, description or comment body is prose by
any reading. The distinction is the one rule 2 already draws — an enumerated value against a value
someone typed into a text box — with the boundary now stated for the case where the enumerated
value happens to be a name.

The cost is knowingly accepted: these names are Jira-authored content living outside the delimited
region. That was already true of the status names rule 2 admits, and the README already declares
the whole structured half untrusted once rather than field by field.

**A paging field is present only when the server was actually given the number.** The software API
does not report a total, so `total` is absent from a board or sprint listing rather than present as
null or as zero. Absence means unknown; zero means none, and a caller cannot be asked to tell those
apart from the same value. The same rule governs any paging field a future endpoint declines to
give.
