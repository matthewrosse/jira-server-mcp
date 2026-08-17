using System.Net;
using JiraServerMcp.JiraIntegration.Tests.Harness;

namespace JiraServerMcp.JiraIntegration.Tests;

/// <summary>
/// Readiness polling, against a stub that reproduces the boot sequence the Phase 0 spike
/// measured. No Jira and no Docker: these run on every pull request.
/// </summary>
public class JiraReadinessTests
{
    private static readonly TimeSpan _noWait = TimeSpan.Zero;

    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The finding that makes this class exist. <c>/status</c> flips to <c>FIRST_RUN</c> about a
    /// hundred seconds before the web layer serves anything, and in between <c>/</c> answers a
    /// redirect to a page that answers 503. A harness polling only <c>/status</c> races the
    /// wizard and fails on its first post.
    /// </summary>
    [Fact]
    public async Task The_wizard_gate_does_not_open_while_status_reports_first_run_but_nothing_serves()
    {
        var jira = new StubJira
        {
            Status = """{"state":"FIRST_RUN"}""",
            RootStatusCodes = [HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK],
        };

        var readiness = new JiraReadiness(jira.Client, _noWait);

        await readiness.WaitForSetupWizardAsync(_budget, TestContext.Current.CancellationToken);

        // It kept polling the root rather than trusting /status alone.
        jira.RootRequests.ShouldBe(3);
    }

    [Fact]
    public async Task The_wizard_gate_waits_for_status_to_answer_at_all()
    {
        var jira = new StubJira
        {
            StatusStatusCodes = [HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK],
            Status = """{"state":"FIRST_RUN"}""",
            RootStatusCodes = [HttpStatusCode.OK],
        };

        var readiness = new JiraReadiness(jira.Client, _noWait);

        await readiness.WaitForSetupWizardAsync(_budget, TestContext.Current.CancellationToken);

        jira.StatusRequests.ShouldBe(2);
    }

    /// <summary>
    /// Early in boot the port is not listening at all, which surfaces as a transport failure
    /// rather than a status code. That is the container still starting, not a broken harness.
    /// </summary>
    [Fact]
    public async Task A_refused_connection_is_treated_as_not_ready_yet_rather_than_as_a_failure()
    {
        var jira = new StubJira
        {
            RefusalsBeforeAnswering = 3,
            Status = """{"state":"FIRST_RUN"}""",
            RootStatusCodes = [HttpStatusCode.OK],
        };

        var readiness = new JiraReadiness(jira.Client, _noWait);

        await readiness.WaitForSetupWizardAsync(_budget, TestContext.Current.CancellationToken);

        jira.RefusalsServed.ShouldBe(3);
    }

    /// <summary>
    /// After setup the instance restarts into a licensed, configured Jira. The second gate is the
    /// one the issue names: status reporting running, then the platform API answering.
    /// </summary>
    [Fact]
    public async Task The_platform_api_gate_waits_for_running_and_then_for_the_api_itself()
    {
        var jira = new StubJira
        {
            StatusBodies = ["""{"state":"STARTING"}""", """{"state":"RUNNING"}"""],
            ServerInfoStatusCodes = [HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK],
        };

        var readiness = new JiraReadiness(jira.Client, _noWait);

        await readiness.WaitForPlatformApiAsync(_budget, TestContext.Current.CancellationToken);

        jira.StatusRequests.ShouldBe(2);
        jira.ServerInfoRequests.ShouldBe(2);
    }

    [Fact]
    public async Task The_platform_api_gate_does_not_accept_first_run_as_running()
    {
        var jira = new StubJira
        {
            Status = """{"state":"FIRST_RUN"}""",
            ServerInfoStatusCodes = [HttpStatusCode.OK],
        };

        var readiness = new JiraReadiness(jira.Client, _noWait);

        await Should.ThrowAsync<TimeoutException>(
            () => readiness.WaitForPlatformApiAsync(
                TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));

        jira.ServerInfoRequests.ShouldBe(0);
    }

    /// <summary>
    /// A Jira that never comes up is the most likely failure of the whole harness, so the message
    /// has to say which gate was still shut and for how long.
    /// </summary>
    [Fact]
    public async Task Exhausting_the_budget_says_which_gate_never_opened()
    {
        var jira = new StubJira
        {
            Status = """{"state":"FIRST_RUN"}""",
            RootStatusCodes = [HttpStatusCode.ServiceUnavailable],
        };

        var readiness = new JiraReadiness(jira.Client, _noWait);

        var thrown = await Should.ThrowAsync<TimeoutException>(
            () => readiness.WaitForSetupWizardAsync(
                TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain("setup wizard");
        thrown.Message.ShouldContain("503");
    }

    /// <summary>
    /// A booting Jira accepts the connection and then does not answer. Left to the HttpClient's own
    /// timeout, one such request blocks the whole poll loop for minutes at a time and the budget is
    /// spent on a handful of attempts — which is how a Jira that came up fine is reported as never
    /// having come up. Each attempt gets its own short deadline instead.
    /// </summary>
    [Fact]
    public async Task A_request_that_hangs_is_abandoned_and_retried_rather_than_blocking_the_budget()
    {
        var jira = new StubJira
        {
            Status = """{"state":"FIRST_RUN"}""",
            RootStatusCodes = [HttpStatusCode.OK],
            // Longer than the per-attempt deadline below, and longer than the whole budget.
            HangFor = TimeSpan.FromSeconds(30),
            HangingRequests = 2,
        };

        var readiness = new JiraReadiness(
            jira.Client, _noWait, attemptTimeout: TimeSpan.FromMilliseconds(200));

        await readiness.WaitForSetupWizardAsync(
            TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        jira.HungRequestsServed.ShouldBe(2);
    }

    [Fact]
    public async Task Cancellation_stops_the_polling()
    {
        var jira = new StubJira
        {
            Status = """{"state":"FIRST_RUN"}""",
            RootStatusCodes = [HttpStatusCode.ServiceUnavailable],
        };

        var readiness = new JiraReadiness(jira.Client, TimeSpan.FromMilliseconds(10));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(
            () => readiness.WaitForPlatformApiAsync(_budget, cancellation.Token));
    }

    /// <summary>
    /// Answers the three endpoints readiness polls, each from a queue of responses so a test can
    /// describe a sequence over time. The last entry repeats once the queue is drained.
    /// </summary>
    private sealed class StubJira : HttpMessageHandler
    {
        public string Status { get; init; } = """{"state":"RUNNING"}""";

        public IReadOnlyList<string>? StatusBodies { get; init; }

        public IReadOnlyList<HttpStatusCode> StatusStatusCodes { get; init; } = [HttpStatusCode.OK];

        public IReadOnlyList<HttpStatusCode> RootStatusCodes { get; init; } = [HttpStatusCode.OK];

        public IReadOnlyList<HttpStatusCode> ServerInfoStatusCodes { get; init; } = [HttpStatusCode.OK];

        public int RefusalsBeforeAnswering { get; init; }

        /// <summary>
        /// How long the first <see cref="HangingRequests"/> requests take to answer — a booting
        /// Jira accepting the connection and then going quiet.
        /// </summary>
        public TimeSpan HangFor { get; init; }

        public int HangingRequests { get; init; }

        public int HungRequestsServed { get; private set; }

        public int StatusRequests { get; private set; }

        public int RootRequests { get; private set; }

        public int ServerInfoRequests { get; private set; }

        public int RefusalsServed { get; private set; }

        public HttpClient Client => new(this) { BaseAddress = new Uri("http://jira.invalid") };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (RefusalsServed < RefusalsBeforeAnswering)
            {
                RefusalsServed++;
                throw new HttpRequestException("Connection refused");
            }

            if (HungRequestsServed < HangingRequests)
            {
                HungRequestsServed++;

                // Connection accepted, no answer coming. The caller's per-attempt deadline is what
                // has to end this.
                await Task.Delay(HangFor, cancellationToken);
            }

            var path = request.RequestUri!.AbsolutePath;

            if (path is "/status")
            {
                var body = StatusBodies is null
                    ? Status
                    : StatusBodies[Math.Min(StatusRequests, StatusBodies.Count - 1)];

                var code = At(StatusStatusCodes, StatusRequests);
                StatusRequests++;

                return new HttpResponseMessage(code)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                };
            }

            if (path.StartsWith("/rest/api/2/serverInfo", StringComparison.Ordinal))
            {
                var code = At(ServerInfoStatusCodes, ServerInfoRequests);
                ServerInfoRequests++;

                return new HttpResponseMessage(code);
            }

            var rootCode = At(RootStatusCodes, RootRequests);
            RootRequests++;

            return new HttpResponseMessage(rootCode);
        }

        private static HttpStatusCode At(IReadOnlyList<HttpStatusCode> codes, int index) =>
            codes[Math.Min(index, codes.Count - 1)];
    }
}
