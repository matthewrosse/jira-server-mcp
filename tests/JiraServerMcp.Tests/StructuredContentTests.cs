using System.Net;
using System.Text.Json;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tests;

/// <summary>
/// The structured half of a rendered result (ADR-0009). These are exact-equality assertions on the
/// serialized shape rather than property spot-checks, because rule 1 promises a contract — a field
/// may be added, never removed and never retyped — and only comparing the whole document can catch
/// a field that quietly changed its name, its type, or its place.
/// </summary>
public class StructuredContentTests
{
    [Fact]
    public void A_page_of_issues_carries_its_rows_its_position_and_where_to_resume()
    {
        var structure = Structure(SearchResults.Render(Page(
            startAt: 0,
            total: 2,
            Issue("PROJ-12", """
                {
                  "summary": "Login fails with a 401",
                  "status": { "id": "3", "name": "In Progress" },
                  "issuetype": { "name": "Bug" },
                  "assignee": { "name": "mrosse", "displayName": "Mateusz Różański" }
                }
                """),
            Issue("PROJ-13", """{ "summary": "Rotate the signing key" }"""))));

        // The display name is prose and the summary is prose; neither is here. The username is an
        // identifier a follow-up JQL can use, so it is.
        structure.ShouldBe(
            """
            {"outcome":"ok","total":2,"startAt":0,"count":2,"cutByBudget":false,"issues":[{"key":"PROJ-12","statusId":"3","status":"In Progress","typeName":"Bug","assignee":"mrosse"},{"key":"PROJ-13"}]}
            """);
    }

    [Fact]
    public void A_page_with_more_behind_it_carries_the_position_to_resume_from()
    {
        var structure = Structure(SearchResults.Render(Page(
            startAt: 25,
            total: 400,
            Issue("PROJ-12", """{ "summary": "One of four hundred" }"""))));

        structure.ShouldBe(
            """
            {"outcome":"ok","total":400,"startAt":25,"count":1,"nextStartAt":26,"cutByBudget":false,"issues":[{"key":"PROJ-12"}]}
            """);
    }

    [Fact]
    public void A_page_cut_by_the_budget_agrees_with_its_prose_on_the_row_count()
    {
        // A page Jira was willing to send whole, whose rows together cost more than a response is
        // worth: the budget, not Jira's paging, is what ends this list.
        var summary = new string('x', ResponseBudget.LineText);

        var issues = Enumerable.Range(1, 400)
            .Select(number => Issue($"PROJ-{number}", $$"""{ "summary": "{{summary}}" }"""))
            .ToArray();

        var rendered = SearchResults.Render(Page(startAt: 0, total: 4_000, issues));
        var page = Deserialize<IssuePageOutput>(rendered);

        // Two halves of one response that disagreed on their row count would be exactly the drift
        // the structured half exists to prevent.
        var count = page.Count.ShouldNotBeNull();

        page.Issues.ShouldNotBeNull().Count.ShouldBe(count);
        count.ShouldBeLessThan(issues.Length);
        page.CutByBudget.ShouldBe(true);

        // And the resume position is where the rows actually stopped, not where Jira's page did.
        page.NextStartAt.ShouldBe(count);
        rendered.Text.ShouldContain($"startAt: {page.NextStartAt}");
    }

    [Fact]
    public void A_bulk_read_carries_every_key_it_was_asked_for_as_a_row_or_a_failure()
    {
        var structure = Structure(BulkIssueDetail.Render(
            [
                Success("PROJ-1"),
                Failure("PROJ-9", new JiraApiException(
                    HttpStatusCode.NotFound,
                    "/rest/api/2/issue/PROJ-9",
                    [],
                    new Dictionary<string, string>())),
                Failure("PROJ-7", new JiraApiException(
                    HttpStatusCode.Forbidden,
                    "/rest/api/2/issue/PROJ-7",
                    ["You do not have permission"],
                    new Dictionary<string, string>())),
            ],
            []));

        // One shape whether or not isError is set, and a per-key outcome for each key that did not
        // come back — a 404 needs no status code, because Jira says nothing more with it.
        structure.ShouldBe(
            """
            {"outcome":"ok","asked":3,"returned":1,"issues":[{"key":"PROJ-1","statusId":"3","status":"In Progress","typeName":"Bug"}],"failures":[{"key":"PROJ-9","outcome":"not_found"},{"key":"PROJ-7","outcome":"jira_api","statusCode":403}]}
            """);
    }

    [Fact]
    public void A_bulk_read_where_nothing_came_back_does_not_report_success()
    {
        var structure = Structure(BulkIssueDetail.Render(
            [
                Failure("PROJ-9", new JiraApiException(
                    HttpStatusCode.NotFound,
                    "/rest/api/2/issue/PROJ-9",
                    [],
                    new Dictionary<string, string>())),
            ],
            []));

        // The tool marks this call an error, and an agent branching on the outcome rather than on
        // the prose — which is the whole point of the structured half — must see the same thing.
        structure.ShouldBe(
            """
            {"outcome":"not_found","asked":1,"returned":0,"issues":[],"failures":[{"key":"PROJ-9","outcome":"not_found"}]}
            """);
    }

    [Fact]
    public void A_bulk_read_that_partly_succeeded_still_reports_success()
    {
        var structure = Deserialize<BulkIssuesOutput>(BulkIssueDetail.Render(
            [
                Success("PROJ-1"),
                Failure("PROJ-9", new JiraApiException(
                    HttpStatusCode.NotFound,
                    "/rest/api/2/issue/PROJ-9",
                    [],
                    new Dictionary<string, string>())),
            ],
            []));

        // A partial answer is a useful one, and the per-key list is where the bad key is read.
        structure.Outcome.ShouldBe("ok");
    }

    [Fact]
    public void A_page_whose_first_row_did_not_fit_offers_nowhere_to_resume_from()
    {
        // One issue whose projection was widened until its single line costs more than the whole
        // response is worth. Each field is cut to its own limit, so it takes many of them.
        var value = new string('x', ResponseBudget.LineText);

        var fields = string.Join(
            ",",
            Enumerable.Range(1, 400).Select(number => $"\"customfield_1{number:0000}\": \"{value}\""));

        var rendered = SearchResults.Render(Page(
            startAt: 0,
            total: 4_000,
            Issue("PROJ-12", $$"""{ {{fields}} }""")));

        var page = Deserialize<IssuePageOutput>(rendered);

        // Answering startAt: 0 here would send the caller to fetch the page it just asked for, and
        // to keep fetching it. The prose says "nothing to show on this page" for the same reason.
        page.Count.ShouldBe(0);
        page.NextStartAt.ShouldBeNull();
        rendered.Text.ShouldContain("nothing to show");
    }

    [Fact]
    public void A_bulk_read_that_wholly_succeeded_still_carries_the_failures_list()
    {
        var structure = Structure(BulkIssueDetail.Render([Success("PROJ-1")], []));

        // Present and empty, not absent: a caller must not have to handle the field appearing and
        // vanishing with the number of bad keys.
        structure.ShouldContain("\"failures\":[]");
    }

    [Fact]
    public void The_create_screen_carries_every_field_a_create_must_send()
    {
        var structure = Structure(CreateFields.Render(new JiraCreateFields(
            "PROJ",
            "Bug",
            [
                new JiraCreateField("summary", "Summary", "string", Required: true, []),
                new JiraCreateField(
                    "customfield_10010",
                    "Severity",
                    "option",
                    Required: true,
                    ["Blocker", "Major", "Minor"]),
                new JiraCreateField("labels", "Labels", "array", Required: false, []),
            ])));

        // The name is a selection label: customfield_10010 tells an agent nothing on its own, and
        // the allowed values are what a create must send verbatim.
        structure.ShouldBe(
            """
            {"outcome":"ok","projectKey":"PROJ","issueTypeName":"Bug","fields":[{"id":"summary","name":"Summary","required":true,"type":"string","hasAllowedValues":false},{"id":"customfield_10010","name":"Severity","required":true,"type":"option","hasAllowedValues":true,"allowedValues":["Blocker","Major","Minor"],"allowedValuesTruncated":false},{"id":"labels","name":"Labels","required":false,"type":"array","hasAllowedValues":false}],"totalFields":3,"fieldsTruncated":false}
            """);
    }

    [Fact]
    public void A_field_whose_schema_jira_omitted_carries_no_type_rather_than_failing()
    {
        var structure = Structure(CreateFields.Render(new JiraCreateFields(
            "PROJ",
            "Bug",
            [new JiraCreateField("summary", "Summary", Type: null, Required: true, [])])));

        // Jira Server versions differ in what schema they return, and a missing one must not turn
        // a good answer into a protocol error — so the field is absent, not null.
        structure.ShouldBe(
            """
            {"outcome":"ok","projectKey":"PROJ","issueTypeName":"Bug","fields":[{"id":"summary","name":"Summary","required":true,"hasAllowedValues":false}],"totalFields":1,"fieldsTruncated":false}
            """);
    }

    [Fact]
    public void A_cut_list_of_allowed_values_says_it_was_cut_and_still_says_it_is_constrained()
    {
        var many = Enumerable.Range(1, CreateFields.ValueCap + 5)
            .Select(number => $"Component {number}")
            .ToArray();

        var field = Deserialize<CreateFieldsOutput>(CreateFields.Render(new JiraCreateFields(
                "PROJ",
                "Bug",
                [new JiraCreateField("components", "Component/s", "array", Required: true, many)])))
            .Fields.ShouldNotBeNull()
            .ShouldHaveSingleItem();

        field.AllowedValues.ShouldNotBeNull().Count.ShouldBe(CreateFields.ValueCap);
        field.AllowedValuesTruncated.ShouldBe(true);

        // "Constrained, but the list was cut" must stay distinguishable from "unconstrained".
        field.HasAllowedValues.ShouldBeTrue();
    }

    /// <summary>
    /// The create screen's worst case is every field carrying a full list of allowed values, which
    /// is the one place in this server where the structured half is the larger of the two.
    /// </summary>
    [Fact]
    public void The_worst_case_structured_half_of_a_create_screen_stays_bounded()
    {
        var values = Enumerable.Range(1, CreateFields.ValueCap)
            .Select(number => $"An allowed value spelled out at length {number}")
            .ToArray();

        var fields = Enumerable.Range(1, CreateFields.OptionalCap + 10)
            .Select(number => new JiraCreateField(
                $"customfield_1{number:0000}",
                $"A custom field with a long administrative name {number}",
                "option",
                Required: false,
                values))
            .ToArray();

        var structure = Structure(CreateFields.Render(new JiraCreateFields("PROJ", "Bug", fields)));

        structure.Length.ShouldBeLessThan(
            120_000,
            "The create screen's structured half has grown past what the caps bound it to. Check "
            + "the value cap and the optional-field cap, which are what make it finite.");
    }

    /// <summary>
    /// Rule 2 admits only short values, and rule 4 makes the structured half inherit the prose's
    /// budget cut, so the structure is bounded by construction. Rule 1 guarantees fields will be
    /// added, and "bounded by construction" stops being true the first time someone adds a
    /// description — which is what this pins.
    /// </summary>
    [Fact]
    public void The_worst_case_structured_half_of_a_page_stays_small()
    {
        var issues = Enumerable.Range(1, ResponseBudget.LargestPageSize)
            .Select(number => Issue($"LONGPROJECTKEY-{number}", """
                {
                  "summary": "Ordinary",
                  "status": { "id": "10001", "name": "Waiting for customer response" },
                  "issuetype": { "name": "Service Request with Approvals" },
                  "assignee": { "name": "a.developer.with.a.long.username" }
                }
                """))
            .ToArray();

        var structure = Structure(SearchResults.Render(
            Page(startAt: 0, total: issues.Length, issues)));

        structure.Length.ShouldBeLessThan(
            24_000,
            "The structured half of a full page has grown past three quarters of the prose "
            + "budget it rides beside. Rule 2 of "
            + "ADR-0009 admits identifiers and enumerated values only — check that a field "
            + "carrying prose has not been added.");
    }

    [Fact]
    public void The_outcome_envelope_carries_the_status_only_where_jira_answered_one()
    {
        Raw(ToolOutputs.Outcome(Outcomes.JiraApi, 403))
            .ShouldBe("""{"outcome":"jira_api","statusCode":403}""");

        Raw(ToolOutputs.Outcome(Outcomes.Unreachable)).ShouldBe("""{"outcome":"unreachable"}""");
        Raw(ToolOutputs.Outcome(Outcomes.TimedOut)).ShouldBe("""{"outcome":"timed_out"}""");
        Raw(ToolOutputs.Outcome(Outcomes.Refused)).ShouldBe("""{"outcome":"refused"}""");
    }

    private static string Structure(Rendered rendered) =>
        Raw(rendered.Structure.ShouldNotBeNull());

    private static string Raw(JsonElement structure) => structure.GetRawText();

    private static T Deserialize<T>(Rendered rendered) =>
        rendered.Structure.ShouldNotBeNull().Deserialize<T>()
        ?? throw new InvalidOperationException("The structured half deserialized to nothing.");

    private static JiraIssue Issue(string key, string fields) =>
        new(key, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fields)!);

    private static JiraSearchPage Page(int startAt, int total, params JiraIssue[] issues) =>
        new(startAt, ResponseBudget.DefaultPageSize, total, issues);

    private static BulkIssueResult Success(string key) =>
        new(
            key,
            new JiraIssueDetail(
                key,
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
                    {
                      "summary": "Login fails",
                      "status": { "id": "3", "name": "In Progress" },
                      "issuetype": { "name": "Bug" }
                    }
                    """)!,
                [],
                null,
                null,
                [],
                null,
                null),
            null);

    private static BulkIssueResult Failure(string key, Exception failure) =>
        new(key, null, failure);
}
