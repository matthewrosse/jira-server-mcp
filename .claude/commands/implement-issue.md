---
description: Implement a GitHub issue end to end, following this repo's conventions, and commit the result
argument-hint: <issue-number>
---

Implement GitHub issue #$ARGUMENTS in jira-server-mcp.

Read the issue body first and treat it as the spec. Several issues here were produced by
grilling sessions and carry explicit acceptance criteria and an out-of-scope list — where
those exist, they are binding. Do not widen the scope. If the issue names something as out of
scope, leaving it undone is the correct outcome, not an oversight.

Before writing code, read `CONTEXT.md` for the domain vocabulary and use those terms in names,
comments, and the commit message. Read any ADR the issue references.

## Repo conventions

- Two projects only (ADR-0003). `JiraServerMcp.Jira` is the typed Jira client with no MCP
  concept in it; `JiraServerMcp` is the host, tools, CLI and composition. One dependency edge,
  host to client. No raw HTTP in a tool class.
- Every tool goes through `ToolCall` rather than growing its own failure ladder or its own
  `Text`/`Error` helpers. `ToolCallConventionTests` pins this.
- Tool registration is data in `ToolSurface`, not control flow in the serve verb.
- Free text authored inside Jira is untrusted content and goes through the shared renderer —
  including the text Jira returns when it refuses a request.
- Response limits come from `ResponseBudget`, not from constants at the call site.
- If the change adds, renames or re-grants a tool, the README tool catalogue must be updated in
  the same change. `ReadmeTests` holds it to `ToolSurface` and will fail otherwise.
- `CONTEXT.md` is a glossary of domain language and nothing else. Add a term only if the work
  introduced a genuinely new domain concept; never put implementation decisions there. Those go
  in an ADR, and only when the decision is hard to reverse, surprising without context, and the
  result of a real trade-off.
- ADR numbers: take the next free number at write time. Several issues are spec'd to write an
  ADR and may claim a number before you do. Never hardcode one from an issue body.
- Match the surrounding style, including XML doc comments that explain why rather than what.
  Look at a neighbouring file before inventing a shape.

## Tests

Place a test at the seam it actually proves. Client behaviour — signing, paging, error mapping,
resilience — is proven in `JiraServerMcp.Jira.Tests` against an HTTP double. Tool behaviour is
proven at the protocol seam in `JiraServerMcp.Tests`. Repo-shape and convention tests live in
`JiraServerMcp.Tests` and use `RepositoryRoot.Find()`; `HostProjectTests` and
`ToolCallConventionTests` are the models to copy.

If a test is a guard that is green the moment you write it, prove it can fail before calling it
done — perturb the threshold or the input, watch it go red, check the failure message reads well
to someone who did not write it, then restore.

## Verify, then commit

Run `dotnet test` and get it green. Do not run integration tests because they're slow. Then re-read the issue's acceptance criteria one at a time
and say for each whether it is met. Report anything you skipped and why rather than quietly
narrowing scope.

Then commit, without asking for approval:

- If the current branch is `main`, create a branch first. Name it for the work, not the issue
  number.
- Stage only the files this work touched. Never `git add -A` — an unrelated dirty file in the
  tree is not yours to commit.
- Write the subject as a sentence naming what changed, in the voice of the existing log:
  "The tool surface as a table, not an if-chain", "Extract response budget policy". No
  `feat:`/`fix:` prefixes. Reference the issue as `(#$ARGUMENTS)` at the end of the subject.
- Use the body to say why, when the why is not obvious from the diff. Domain vocabulary from
  `CONTEXT.md`, prose, no bullet-point dumps of the diff.
- No `Co-Authored-By` line and no `Claude-Session` line.

Do not push and do not open a PR. Report the branch name and the commit subject when done.

At the end of work do a code review using /code-review medium skill, then fix found issues.

If everything is fine, then create a PR.
