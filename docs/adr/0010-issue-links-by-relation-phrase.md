# ADR-0010: Issue links are made by relation phrase

**Status:** Accepted (2026-08-18)

## Context

Jira's `POST /rest/api/2/issueLink` takes a link type by name and two issues, one in an
`inwardIssue` slot and one in an `outwardIssue` slot. The type is named twice — once from each
end: `Blocks` publishes `outward: "blocks"` and `inward: "is blocked by"` — and the endpoint reads
the link as *outward issue does the outward wording to the inward issue*.

So a caller asking for "PROJ-1 blocks PROJ-2" must know three things at once: that the type is
called `Blocks`, that "blocks" is its outward wording, and that the outward slot therefore takes
PROJ-1. A caller asking for the same link from the other side — "PROJ-1 is blocked by PROJ-2" —
must reverse the keys. Getting it wrong does not fail: Jira accepts the reversed link happily and
puts a relation on the panel that says the opposite of what was meant, on two issues, silently.
This is the standing bug of every Jira integration that exposes linking, and documenting the
direction has never been enough to stop it.

The obvious tool signature — `type` plus `direction`, mirroring Jira's own — inherits the problem
whole. A tool taking a type and two keys and always linking `from → to` outward inherits half of
it: the inward phrasing becomes unsayable, so callers phrase the sentence backwards in their heads
instead of in the arguments.

## Decision

`jira_link_issues` takes `from`, `to`, and a **relation phrase** — the direction-specific wording
Jira itself publishes for a type — and the phrase decides which key goes in which slot.

`jira_link_issues(from: "PROJ-1", to: "PROJ-2", relation: "blocks")` and
`jira_link_issues(from: "PROJ-2", to: "PROJ-1", relation: "is blocked by")` are the same link, both
read as English, and neither can be got backwards, because there is no argument whose meaning
depends on knowing which end Jira calls outward. The direction confusion is not documented away;
it is made unrepresentable.

Resolving the phrase requires `GET /rest/api/2/issueLinkType` before every link. That read is
**not** a safety pre-flight in the manner of `jira_transition_issue`'s transition list — it is how
the argument is interpreted. Jira's endpoint takes `type.name` and two slots, and there is no way
to fill either without having resolved the phrase first. Recorded here so it does not later look
cargo-culted.

Matching follows `TransitionIssueTool`: casing and surrounding space ignored, and three outcomes.

- **Symmetric.** A phrase that is both the inward and the outward wording of one type — `Relates`
  publishes "relates to" on both sides — has no direction to choose, so `from` is sent as outward.
- **Ambiguous.** A phrase that is the wording of two different types, which a Jira with locally
  added link types can have, links nothing and names both. Picking either would invent the
  caller's intent and put a relation on the panel it did not ask for.
- **Unmatched.** A phrase matching nothing lists the phrases this Jira does publish, both wordings
  of each. Every one of those lists is Jira-authored, so it is framed as untrusted content.

## Consequences

- Every link costs two requests. The type list is small and Jira-cached, and the alternative is the
  bug this ADR exists to remove.
- The phrases are per-instance. A Jira with local link types is served by the same tool without
  this server carrying a table of Atlassian's defaults, which would go stale against exactly the
  instances that have customised them.
- A caller that already knows the type name cannot say so. It says the wording instead, which the
  unmatched case hands it for free.
- There is no unlink tool at any grant, so a reversed link — which this design makes very hard to
  produce, not impossible — is a human's cleanup in Jira.
- **`jira_update_issue` refuses `issuelinks` in its own add and remove maps**, and points here
  instead. Jira takes a link through the `update` envelope of an ordinary issue edit — verified on
  8.20.7, which answers `204` — so this is this server's choice and the refusal says so. What the
  envelope takes is the raw inward and outward slot pair, with no phrase to resolve, which is the
  second door onto exactly the bug this decision closed.

## Not in this decision

The two choices that came up beside it, recorded so their absence is not read as an oversight:

- **The `links:write` grant** is a grant of its own rather than part of `issues:write`. The grant
  is the blast-radius unit, and "attach your pull request" is precisely a capability an operator
  wants to hand out alone, without also handing out "restructure the epic tree". Ordinary
  application of ADR-0005, not a decision this ADR needed to make.
- **A remote link's `globalId` is the URL itself**, bare and unnamespaced. Atlassian's
  `system=…&id=…` convention keeps independent producers from colliding on one identifier space;
  here the collision is the point, because one link per URL on an issue is the correct end state
  and a namespaced identifier would attach a second copy of a pull request another integration
  already attached.
