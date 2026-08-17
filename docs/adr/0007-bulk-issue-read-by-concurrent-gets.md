# ADR-0007: Bulk issue read by concurrent GETs, not one JQL search

**Status:** Accepted (2026-08-17)

## Context

`jira_search` already reaches several issues in one call: `key in (PROJ-1, PROJ-2, PROJ-3)` is
ordinary JQL, and it returns those issues with the same field projection and the same `fields`
widening a bulk read would offer. A reader who knows that JQL exists will ask why #55 added a
second tool rather than teaching `jira_search` to accept a key list.

## Decision

`jira_get_issues` fans out N concurrent `GET /rest/api/2/issue/{key}` requests, capped at five in
flight, rather than sending one `key in (...)` search.

A JQL search is one request with one outcome: it fails whole, and Jira 8 fails the whole query on
a single key that does not exist or is not visible — one bad key among twenty costs the caller all
twenty. It also has no `expand`. Comments, transitions, changelog, links and worklogs are
per-issue sections reachable only through the single-issue endpoint, and a bulk read exists
specifically to gather those for several issues without a call per key. A search that happened to
match the same keys could not offer them at all.

Reusing the single-issue endpoint per key, rather than adding a bulk-shaped endpoint call, also
keeps expansion behaviour identical between a one-key call and a twenty-key one: both run
`JiraClient.GetIssueAsync`, so there is exactly one place that code path can drift.

## Consequences

- A key that does not exist or is not visible fails alone; the other nineteen still render.
- Every expansion `jira_get_issue` offered is available in bulk, with no second implementation to
  keep in step with the first.
- Twenty keys cost twenty round trips to Jira rather than one, running four waves deep at the
  concurrency cap. This is the trade this ADR makes deliberately: the expensive thing being
  removed is the agent's round trips, not Jira's, and `JiraRetryHandler`'s existing 408/429 retries
  are what stands between this and an ageing Jira behind a reverse proxy.
- A profile-level auth failure (401/403) is not folded into a per-key outcome. If the token is
  dead, every key is doomed, and the whole call fails the same way a single-issue read's does
  rather than returning twenty identical per-key lines.
