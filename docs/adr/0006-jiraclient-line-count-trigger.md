# ADR-0006: A line-count trigger for splitting JiraClient

**Status:** Accepted (2026-08-17)

## Context

`JiraClient` (`src/JiraServerMcp.Jira/JiraClient.cs`) is a flat, sealed class with one method
per REST endpoint. It is not shallow — each method does real request and response work, and it
passes the deletion test: removing it would concentrate complexity rather than remove it. It is
simply the one module in the client with no internal seam, so every new tool adds a method to
the same file.

ADR-0003 already refused premature layering once, at the project level. Splitting an 18-method
class that still passes the deletion test would be the same mistake at a smaller scale. But
deferring the split needs a trigger that fires on its own, not a threshold written as prose in
an issue nobody re-reads.

The original hunch, from the 2026-08-17 architecture review, set the trigger at roughly 30
methods, or a new batch of tools. Neither is met by the open tool work (#54, #55), and method
count turns out to be the wrong measure anyway: the cost being guarded against is the context a
reader — human or agent — must load to change one small part of the file, and that is a function
of lines, not methods. Eighteen methods averaging thirty lines cost more to load than thirty
methods averaging three; several of `JiraClient`'s current methods are one-line delegations to
`GetAsync<T>` that cost a method and almost nothing to read.

## Decision

Do not split `JiraClient` now. Instead, pin a line-count trigger with a test:
`tests/JiraServerMcp.Tests/JiraClientGrowthTests.cs` sums the raw line count of every file
matching `JiraClient*.cs` — including XML doc comments, since those are loaded too — and asserts
the total stays under **800** lines.

The glob, not a single file, is what's measured. If the limit applied to `JiraClient.cs` alone,
the test would destroy itself the moment it succeeded: a partial split would drop that one file
under a hundred lines and the guard would silently stop guarding anything. Summing the glob
means the trigger survives its own remedy and fires again if the client as a whole keeps
growing.

800 leaves roughly one feature batch of headroom over `JiraClient.cs`'s size at the time this
ADR was written. A tighter number would fire as noise on ordinary work; a looser one would fire
only after the file was already unpleasant.

When the test goes red, two responses are allowed:

1. **Split** `JiraClient` into `partial class JiraClient` files along the resource axis: issues,
   projects, users, agile, writes, and a core file holding the shared private helpers. This
   mirrors the boundary the test tree already uses — `JiraSearchTests`, `JiraClientProjectTests`,
   `JiraAgileTests`, `JiraWriteTests`, `JiraMetadataTests` — so the split has effectively been
   designed already and the source has not caught up. `partial` keeps the public surface and
   every call site unchanged, which matters because ADR-0003 leaves the door open to publishing
   `JiraServerMcp.Jira` as its own package. Composition — `client.Issues.GetAsync(...)` over
   sub-client objects — is rejected: it solves a coupling problem this client does not have (the
   only shared state is the private helpers) at the cost of breaking every call site and the
   package's public API.
2. **Amend this ADR** to move the number, recording why. A justified threshold change is a
   legitimate outcome; a constant edited in passing on a red build is not.

## Consequences

- `JiraClient.cs` is untouched by this decision. The trigger is a future gate, not a change.
- The threshold's job is to force a decision, not to force a split — raising it is as valid an
  outcome as splitting, as long as it is deliberate and recorded here.
- This does not become a repo-wide per-file line cap. It is about the one class with no internal
  seam, not a general style rule.
