using JiraServerMcp.JiraIntegration.Tests.Harness;

// One Jira for the whole assembly: bringing one up costs minutes, and every Jira-backed class
// wants the same seeded instance. The fixture provisions lazily, so the pull-request run — which
// selects only the parser and readiness tests — starts no container.
[assembly: AssemblyFixture(typeof(JiraHarness))]

// There is one Jira, and the Jira-backed classes write to it. Running them in parallel makes both
// the instance and the assertions race — a shared workflow, a shared project, and a single set of
// credentials being logged in with from several processes at once.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
