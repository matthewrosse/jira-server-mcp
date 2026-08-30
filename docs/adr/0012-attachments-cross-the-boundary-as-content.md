# ADR-0012: An attachment crosses the boundary as content, never as a path

**Status:** Accepted (2026-08-31)

## Context

Until #127 this server could read an attachment and not write one, and the README explained the
absence by pointing at a question it had not answered: file paths crossing the MCP boundary are a
path-traversal surface that "deserves its own design pass and its own ADR".

The upload had to answer that question one way or the other, because the two shapes available are
not variations on one design.

**A path.** `jira_add_attachment(key, path)`. The agent names a file on the machine this server
runs on; the server opens it and streams it to Jira. It costs about forty tokens regardless of the
file's size, which for the artefacts an agent actually attaches — a test log, a coverage report, a
diff, all of which already sit on its disk — is the whole of the cost. In exchange, this server
gains a filesystem: it resolves a caller-supplied string against the disk, and everything that has
ever gone wrong with that becomes possible here. The honest version needs an operator-given sandbox
root (`--attach-root`), symlink resolution, a canonicalisation that agrees with the operating
system's, and a per-platform test suite for the cases where it does not.

**Content.** `jira_add_attachment(key, fileName, content)`. The agent emits the bytes as a tool
argument. Where the artefact is already on the agent's disk this costs *twice* the file — read into
the context window, echoed back out — against a path's forty tokens.

It is worth being precise about what the inline shape does **not** buy, because the first draft of
#127 claimed it and was wrong. It does not save write-side tokens over pasting the same text into a
comment: the agent emits every byte either way. The write-side economics are strictly worse than a
path's, and that is the price of this decision rather than an argument for it.

## Decision

The content crosses the boundary as a string. **This server opens no file on the machine it runs
on**, in either direction: `jira_get_attachment` answers with text in the response rather than
writing a file, and `jira_add_attachment` takes text in the request rather than reading one. A file
name is a label a human will later see on the issue, never a location, and nothing here resolves
one.

The path-traversal question is therefore deleted rather than answered. There is no sandbox root to
configure, no canonicalisation to get right, and no per-platform behaviour to test, because there is
no `File.Open` to reach.

Three consequences follow from the shape and are part of the decision:

- **A size cap, stated by the tool.** 64,000 characters, in the tool that takes them rather than in
  the response budget — that module bounds what an answer costs an agent, and this bounds what a
  request may carry. The cap does not save the agent anything: by the time this server sees the
  content those tokens are spent. What it buys is this server's own sentence, naming the limit and
  the actual size, in place of a Jira 413 or a silent four-megabyte success.
- **One media type: `text/plain; charset=utf-8`.** Not derived from the file name. An
  agent-authored `.html` or `.svg` stored under its "real" type and served by Jira is a stored
  cross-site-scripting shape and is inert as `text/plain`; the read side already documents
  `mimeType` as advisory and branches on nothing, so declaring a type this server refuses to trust
  would be theatre; and an extension table would be a second thing to keep in sync for a benefit
  the file name already gives a human. The accepted cost is that a `report.csv` may open in a
  browser tab rather than download.
- **The file name is validated.** It lands in a multipart `Content-Disposition` header, which is
  the one local attack surface the inline shape does have. Empty, any control character, `/` or
  `\`, exactly `.` or `..`, and longer than 255 characters are refused, each with its own sentence.
  Everything else is accepted — spaces and Unicode included — because it is a label rather than a
  path.

## Consequences

- The README's *Known limitations* bullet stops promising a future ADR and points here. What it
  names as absent is what is actually absent: text only, one file per call, and no replacing or
  deleting an attachment.
- An agent attaching a file it produced pays for it twice. That is the cost this decision buys the
  property with, and it is the reason the cap exists.
- Rejected: a path plus an operator-given sandbox root. Cheaper in tokens, and it hands this server
  a filesystem it currently does not have. The rule above is worth more than the tokens.
- Rejected: taking several files in one call. Jira's endpoint accepts a multipart body with many
  parts, and a list would multiply the failure modes — partial success across N files, per-file size
  accounting, which ones landed after a timeout — for a case nobody has. Two calls are one extra
  round trip.
- This does not reopen if an agent one day wants to attach a screenshot. Binary content is not
  expressible as a string argument, and the answer to that request is not a path — it is a decision
  taken again, on its own evidence, with this ADR's rule as the thing being traded away.
