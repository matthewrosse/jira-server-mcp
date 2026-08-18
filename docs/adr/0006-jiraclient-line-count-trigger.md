# ADR-0006: A line-count trigger for splitting JiraClient

**Status:** Accepted (2026-08-17), amended (2026-08-18)

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

## Amendment (2026-08-18): the threshold moves to 1,000

The trigger fired on the first batch to arrive after it was written — the two link tools of #68,
which added the link-type read, the two writes, and the remote-link read to `JiraClient`, taking
the glob total from 636 to 867.

The threshold moves to **1,000** rather than the split being taken now, for two reasons.

The first is that the split would not have answered the test. What is measured is the glob, on
purpose, so that the guard survives its own remedy — and that means a split leaves the total
exactly where it was, plus a few lines of duplicated `using` blocks. Splitting under a red build
here would have been a refactor that made no test green, ridden along on a feature change, which
is precisely the shape of change this project's conventions refuse.

The second is that the class is unchanged in kind. It is still one method per REST endpoint, still
has no internal seam, and still passes the deletion test. What grew is the number of endpoints,
which is what a client of a growing API does.

1,000 leaves roughly the same headroom over 867 that 800 left over 636: about one more feature
batch. The remaining tool work on the backlog will spend it.

**The next firing is to be answered with the split, not with another number.** Two deferrals in a
row would make this a ratchet that only ever moves in one direction, which is a guard that guards
nothing. When it goes red again, take response 1 above, on its own commit, and set the threshold
from what the split actually measures.

## Amendment (2026-08-18): the split, taken

The trigger fired again on #73's attachment work, which took the glob from 899 to 1,017. As the
previous amendment committed, this was answered with response 1 rather than a third number.

`JiraClient` is now `partial` across a file per resource — account, issues, projects, users, agile,
writes, links — over a core file holding the shared request helpers and the one transport limit.
The public surface is unchanged, and so is every call site, which is what `partial` was chosen for.
The boundary is the one the test tree already used, so nothing about it is new; the source has
simply caught up.

The split measures 984 lines across ten files, against 899 before it: the increase is the using
blocks each file repeats, exactly as the previous amendment predicted. Two thresholds replace the
one:

- **The glob stays, at 1,150.** Roughly one feature batch of headroom over 984, which is the
  headroom 800 left over 636 and 1,000 left over 867. It still guards the client as a whole, and it
  still survives a further split.
- **No single `JiraClient*.cs` file may reach 250 lines.** This is the guard the split makes
  meaningful and the sum cannot express: the cost being managed is what a reader loads to change
  one part, and after a split that is a property of the largest file. The largest today is
  `JiraClientWrites.cs` at 203.

Answering a red build on either is the same as before: add the file the resource wants, or amend
this ADR deliberately. What is no longer available is editing a constant in passing.

