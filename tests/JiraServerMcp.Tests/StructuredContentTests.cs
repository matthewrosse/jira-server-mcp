using System.Net;
using System.Text.Json;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using JiraServerMcp.Tools;

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
                  "assignee": { "name": "ada", "displayName": "Ada Lovelace" }
                }
                """),
            Issue("PROJ-13", """{ "summary": "Rotate the signing key" }"""))));

        // The display name is prose and the summary is prose; neither is here. The username is an
        // identifier a follow-up JQL can use, so it is.
        structure.ShouldBe(
            """
            {"outcome":"ok","total":2,"startAt":0,"count":2,"cutByBudget":false,"issues":[{"key":"PROJ-12","statusId":"3","status":"In Progress","typeName":"Bug","assignee":"ada"},{"key":"PROJ-13"}]}
            """);
    }

    [Fact]
    public void A_change_feed_page_carries_the_watermark_the_next_call_resumes_from()
    {
        var structure = Structure(SearchResults.Render(
            Page(
                startAt: 0,
                total: 1,
                Issue("PROJ-12", """
                    {
                      "summary": "Login fails with a 401",
                      "status": { "id": "3", "name": "In Progress" },
                      "updated": "2026-08-18T09:31:47.412+0200"
                    }
                    """)),
            kept => ChangeFeed.NextSince(
                kept,
                new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.FromHours(2)),
                TimeSpan.FromHours(2))));

        // A watermark is a paging position by another name, so it sits beside the other one. The
        // offset's plus arrives as \u002B: the serializer escapes it, and any JSON reader hands
        // the caller back the timestamp this server wrote.
        structure.ShouldBe(
            """
            {"outcome":"ok","total":1,"startAt":0,"count":1,"cutByBudget":false,"nextSince":"2026-08-18T09:31:00\u002B02:00","issues":[{"key":"PROJ-12","statusId":"3","status":"In Progress"}]}
            """);
    }

    [Fact]
    public void A_change_feed_page_cut_by_the_budget_does_not_move_the_watermark_past_the_rows_it_cut()
    {
        // The rows the budget dropped are rows the caller never saw. A watermark taken from the
        // page rather than from the rows rendered would move the window past them for good.
        var summary = new string('x', ResponseBudget.LineText);

        var issues = Enumerable.Range(1, 400)
            .Select(number => Issue(
                $"PROJ-{number}",
                $$"""
                  {
                    "summary": "{{summary}}",
                    "updated": "2026-08-18T09:{{number % 60:00}}:00.000+0200"
                  }
                  """))
            .ToArray();

        var rendered = SearchResults.Render(
            Page(startAt: 0, total: 4_000, issues),
            kept => ChangeFeed.NextSince(
                kept,
                new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.FromHours(2)),
                TimeSpan.FromHours(2)));

        var page = Deserialize<IssuePageOutput>(rendered);
        var count = page.Count.ShouldNotBeNull();

        count.ShouldBeLessThan(issues.Length);

        var latestRendered = issues.Take(count)
            .Select(issue => issue.Updated.ShouldNotBeNull())
            .Max(StringComparer.Ordinal)
            .ShouldNotBeNull();

        page.NextSince.ShouldBe($"2026-08-18T{latestRendered[11..16]}:00+02:00");
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
                new JiraScreenField("summary", "Summary", "string", Required: true, [], ["set"]),
                new JiraScreenField(
                    "customfield_10010",
                    "Severity",
                    "option",
                    Required: true,
                    ["Blocker", "Major", "Minor"],
                    ["set"]),
                new JiraScreenField(
                    "labels",
                    "Labels",
                    "array",
                    Required: false,
                    [],
                    ["add", "set", "remove"]),
            ]),
            FieldAliases.None));

        // The name is a selection label: customfield_10010 tells an agent nothing on its own, and
        // the allowed values are what a create must send verbatim.
        structure.ShouldBe(
            """
            {"outcome":"ok","projectKey":"PROJ","issueTypeName":"Bug","fields":[{"id":"summary","name":"Summary","required":true,"type":"string","hasAllowedValues":false,"operations":["set"]},{"id":"customfield_10010","name":"Severity","required":true,"type":"option","hasAllowedValues":true,"allowedValues":["Blocker","Major","Minor"],"allowedValuesTruncated":false,"operations":["set"]},{"id":"labels","name":"Labels","required":false,"type":"array","hasAllowedValues":false,"operations":["add","set","remove"]}],"totalFields":3,"fieldsTruncated":false}
            """);
    }

    [Fact]
    public void The_edit_screen_carries_the_key_and_what_may_be_done_to_each_field()
    {
        var structure = Structure(EditFields.Render(new JiraEditFields(
                "PROJ-42",
                [
                    new JiraScreenField("summary", "Summary", "string", Required: true, [], ["set"]),
                    new JiraScreenField(
                        "issuetype",
                        "Issue Type",
                        "issuetype",
                        Required: true,
                        [],
                        []),
                    new JiraScreenField(
                        "duedate",
                        "Due Date",
                        "date",
                        Required: false,
                        [],
                        Operations: null),
                ]),
            FieldAliases.None));

        // An empty list is a real answer — the field is on the screen and cannot be written. A
        // field Jira said nothing about carries no operations at all, which is not that claim.
        structure.ShouldBe(
            """
            {"outcome":"ok","key":"PROJ-42","fields":[{"id":"summary","name":"Summary","required":true,"type":"string","hasAllowedValues":false,"operations":["set"]},{"id":"issuetype","name":"Issue Type","required":true,"type":"issuetype","hasAllowedValues":false,"operations":[]},{"id":"duedate","name":"Due Date","required":false,"type":"date","hasAllowedValues":false}],"totalFields":3,"fieldsTruncated":false}
            """);
    }

    [Fact]
    public void An_edit_screen_with_many_fields_keeps_every_required_one_and_says_the_rest_were_cut()
    {
        var fields = Enumerable.Range(1, ScreenFields.OptionalCap + 10)
            .Select(number => new JiraScreenField(
                $"customfield_1{number:0000}",
                $"Field {number}",
                "string",
                Required: false,
                [],
                ["set"]))
            .ToArray();

        var screen = Deserialize<EditFieldsOutput>(EditFields.Render(
            new JiraEditFields(
                "PROJ-42",
                [
                    new JiraScreenField("summary", "Summary", "string", Required: true, [], ["set"]),
                    .. fields,
                ]),
            FieldAliases.None));

        screen.TotalFields.ShouldBe(fields.Length + 1);
        screen.FieldsTruncated.ShouldBe(true);

        // The required one is never cut, and it leads.
        screen.Fields.ShouldNotBeNull().Count.ShouldBe(ScreenFields.OptionalCap + 1);
        screen.Fields[0].Id.ShouldBe("summary");
    }

    [Fact]
    public void A_field_whose_schema_jira_omitted_carries_no_type_rather_than_failing()
    {
        var structure = Structure(CreateFields.Render(new JiraCreateFields(
            "PROJ",
            "Bug",
            [new JiraScreenField("summary", "Summary", Type: null, Required: true, [], ["set"])]),
            FieldAliases.None));

        // Jira Server versions differ in what schema they return, and a missing one must not turn
        // a good answer into a protocol error — so the field is absent, not null.
        structure.ShouldBe(
            """
            {"outcome":"ok","projectKey":"PROJ","issueTypeName":"Bug","fields":[{"id":"summary","name":"Summary","required":true,"hasAllowedValues":false,"operations":["set"]}],"totalFields":1,"fieldsTruncated":false}
            """);
    }

    [Fact]
    public void A_cut_list_of_allowed_values_says_it_was_cut_and_still_says_it_is_constrained()
    {
        var many = Enumerable.Range(1, ScreenFields.ValueCap + 5)
            .Select(number => $"Component {number}")
            .ToArray();

        var field = Deserialize<CreateFieldsOutput>(CreateFields.Render(new JiraCreateFields(
                "PROJ",
                "Bug",
                [new JiraScreenField("components", "Component/s", "array", Required: true, many, ["set"])]),
                FieldAliases.None))
            .Fields.ShouldNotBeNull()
            .ShouldHaveSingleItem();

        field.AllowedValues.ShouldNotBeNull().Count.ShouldBe(ScreenFields.ValueCap);
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
        var values = Enumerable.Range(1, ScreenFields.ValueCap)
            .Select(number => $"An allowed value spelled out at length {number}")
            .ToArray();

        var fields = Enumerable.Range(1, ScreenFields.OptionalCap + 10)
            .Select(number => new JiraScreenField(
                $"customfield_1{number:0000}",
                $"A custom field with a long administrative name {number}",
                "option",
                Required: false,
                values,
                ["set"]))
            .ToArray();

        var structure = Structure(CreateFields.Render(new JiraCreateFields("PROJ", "Bug", fields), FieldAliases.None));

        structure.Length.ShouldBeLessThan(
            120_000,
            "The create screen's structured half has grown past what the caps bound it to. Check "
            + "the value cap and the optional-field cap, which are what make it finite.");
    }

    [Fact]
    public void A_project_listing_carries_the_keys_every_other_tool_takes_as_input()
    {
        var structure = Structure(ProjectList.Render(
            [
                new JiraProject("PROJ", "Platform", "10100", "software"),
                new JiraProject("OPS", "Operations", "10200", null),
            ]));

        structure.ShouldBe(
            """
            {"outcome":"ok","count":2,"totalCount":2,"cutByCap":false,"projects":[{"key":"PROJ","id":"10100","name":"Platform"},{"key":"OPS","id":"10200","name":"Operations"}]}
            """);
    }

    [Fact]
    public void A_project_listing_the_cap_cut_says_so_and_says_how_many_there_were()
    {
        var projects = Enumerable.Range(1, ResponseBudget.ProjectListCap + 37)
            .Select(number => new JiraProject($"P{number}", $"Project {number}", $"{number}", null))
            .ToArray();

        var listing = Deserialize<ProjectListOutput>(ProjectList.Render(projects));

        // There is no next page to offer — Jira answers with every project at once — so what the
        // structure owes the caller is the true number and the fact that it was cut.
        listing.Count.ShouldBe(ResponseBudget.ProjectListCap);
        listing.TotalCount.ShouldBe(projects.Length);
        listing.CutByCap.ShouldBe(true);
        listing.Projects.ShouldNotBeNull().Count.ShouldBe(ResponseBudget.ProjectListCap);
    }

    [Fact]
    public void A_project_carries_the_names_a_create_call_must_send_verbatim()
    {
        var structure = Structure(ProjectDetail.Render(new JiraProjectDetail(
            new JiraProject("PROJ", "Platform", "10100", "software"),
            Description: "A description, which is prose and is not carried.",
            Lead: "ada",
            IssueTypes:
            [
                new JiraIssueTypeStatuses("1", "Bug", false, [new JiraStatus("3", "In Progress")]),
                new JiraIssueTypeStatuses("2", "Task", false, []),
            ],
            Components: [new JiraProjectComponent("100", "api", "The API, which is prose too.")],
            Versions: [new JiraProjectVersion("200", "1.4.0", true, false, "2026-01-01")])));

        // The lead and the description are absent by decision: nothing branches on the first, and
        // the second is prose, which lives in the delimited region and nowhere else.
        structure.ShouldBe(
            """
            {"outcome":"ok","key":"PROJ","id":"10100","name":"Platform","issueTypeNames":["Bug","Task"],"issueTypeCount":2,"issueTypesTruncated":false,"versionNames":["1.4.0"],"versionCount":1,"versionsTruncated":false,"componentNames":["api"],"componentCount":1,"componentsTruncated":false}
            """);
    }

    [Fact]
    public void A_capped_project_section_carries_the_true_count_beside_the_entries_it_kept()
    {
        var versions = Enumerable.Range(1, ResponseBudget.ProjectSectionCap + 164)
            .Select(number => new JiraProjectVersion($"{number}", $"1.{number}.0", true, false, null))
            .ToArray();

        var project = Deserialize<ProjectDetailOutput>(ProjectDetail.Render(new JiraProjectDetail(
            new JiraProject("PROJ", "Platform", "10100", null),
            null,
            null,
            [],
            [],
            versions)));

        project.VersionCount.ShouldBe(versions.Length);
        project.VersionsTruncated.ShouldBe(true);

        var names = project.VersionNames.ShouldNotBeNull();

        names.Count.ShouldBe(ResponseBudget.ProjectSectionCap);

        // Jira orders versions oldest first, and the prose keeps the most recent because those are
        // the ones a create would name. The structured half keeps exactly the same ones.
        names[^1].ShouldBe(versions[^1].Name);
    }

    /// <summary>
    /// A project with the cap filled in every section, which is the largest a project detail's
    /// structured half can be.
    /// </summary>
    [Fact]
    public void The_worst_case_structured_half_of_a_project_stays_bounded()
    {
        var cap = ResponseBudget.ProjectSectionCap;

        var structure = Structure(ProjectDetail.Render(new JiraProjectDetail(
            new JiraProject("PROJ", "Platform", "10100", null),
            null,
            null,
            [
                .. Enumerable.Range(1, cap).Select(number => new JiraIssueTypeStatuses(
                    $"{number}",
                    $"An issue type with a long administrative name {number}",
                    false,
                    [])),
            ],
            [
                .. Enumerable.Range(1, cap).Select(number => new JiraProjectComponent(
                    $"{number}",
                    $"a-component-with-a-long-name-{number}",
                    "A description, which is not carried.")),
            ],
            [
                .. Enumerable.Range(1, cap).Select(number => new JiraProjectVersion(
                    $"{number}",
                    $"2026.{number}.0-release-candidate",
                    true,
                    false,
                    null)),
            ])));

        structure.Length.ShouldBeLessThan(
            8_000,
            "A project's structured half has grown past what its section caps bound it to. Check "
            + "that a description or another piece of prose has not been added.");
    }

    [Fact]
    public void A_page_of_boards_carries_its_rows_and_where_to_resume_but_no_total()
    {
        var structure = Structure(BoardList.Render(new JiraAgilePage<JiraBoard>(
            0,
            2,
            IsLast: false,
            [new JiraBoard(42, "PROJ board", "scrum"), new JiraBoard(43, "Ops board", null)])));

        // The software API does not report how many boards exist, so no total is carried — not a
        // null one either. Absence means unknown; zero would mean none.
        structure.ShouldBe(
            """
            {"outcome":"ok","startAt":0,"count":2,"nextStartAt":2,"boards":[{"id":42,"name":"PROJ board","type":"scrum"},{"id":43,"name":"Ops board"}]}
            """);

        structure.ShouldNotContain("total");
    }

    [Fact]
    public void A_last_page_of_boards_offers_nowhere_to_resume()
    {
        var structure = Structure(BoardList.Render(new JiraAgilePage<JiraBoard>(
            10,
            50,
            IsLast: true,
            [new JiraBoard(42, "PROJ board", "scrum")])));

        structure.ShouldBe(
            """
            {"outcome":"ok","startAt":10,"count":1,"boards":[{"id":42,"name":"PROJ board","type":"scrum"}]}
            """);
    }

    [Fact]
    public void A_page_of_sprints_carries_the_state_and_not_the_dates()
    {
        var structure = Structure(SprintList.Render(new JiraAgilePage<JiraSprint>(
            0,
            50,
            IsLast: true,
            [new JiraSprint(118, "Sprint 14", "active", "2026-08-01", "2026-08-15")])));

        // state answers "which sprint is current", which is the known use. The dates would make a
        // format this server neither controls nor normalises into a permanent contract.
        structure.ShouldBe(
            """
            {"outcome":"ok","startAt":0,"count":1,"sprints":[{"id":118,"name":"Sprint 14","state":"active"}]}
            """);
    }

    [Fact]
    public void An_empty_agile_page_that_is_not_the_last_resumes_past_it_not_at_it()
    {
        var structure = Deserialize<BoardListOutput>(BoardList.Render(
            new JiraAgilePage<JiraBoard>(20, 10, IsLast: false, [])));

        // Jira filters a page by permission after paging it, so an empty page mid-listing is
        // ordinary — and resuming at 20 would ask for the same nothing for ever.
        structure.Count.ShouldBe(0);
        structure.NextStartAt.ShouldBe(30);
    }

    [Fact]
    public void An_agile_page_cut_by_the_budget_agrees_with_its_prose_on_the_row_count()
    {
        var name = new string('x', ResponseBudget.LineText);

        var boards = Enumerable.Range(1, 400)
            .Select(number => new JiraBoard(number, $"{name} {number}", "scrum"))
            .ToArray();

        var rendered = BoardList.Render(new JiraAgilePage<JiraBoard>(0, 400, IsLast: true, boards));
        var page = Deserialize<BoardListOutput>(rendered);

        var count = page.Count.ShouldNotBeNull();

        page.Boards.ShouldNotBeNull().Count.ShouldBe(count);
        count.ShouldBeLessThan(boards.Length);

        // A budget cut resumes at the row it stopped on, and the prose says the same number.
        page.NextStartAt.ShouldBe(count);
        rendered.Text.ShouldContain($"startAt: {count}");
    }

    /// <summary>
    /// A full page of boards with long administrative names, which is the largest a software-API
    /// listing's structured half can be.
    /// </summary>
    [Fact]
    public void The_worst_case_structured_half_of_an_agile_page_stays_small()
    {
        var boards = Enumerable.Range(1, ResponseBudget.LargestPageSize)
            .Select(number => new JiraBoard(
                100_000 + number,
                $"A board named for the team and the programme it belongs to {number}",
                "scrum"))
            .ToArray();

        var structure = Structure(BoardList.Render(
            new JiraAgilePage<JiraBoard>(0, boards.Length, IsLast: false, boards)));

        structure.Length.ShouldBeLessThan(
            16_000,
            "A software-API page's structured half has grown past what a page of rows should "
            + "cost. Rule 2 admits identifiers and selection labels — check that nothing longer "
            + "has been added.");
    }

    [Fact]
    public void A_user_search_carries_the_usernames_a_write_must_send()
    {
        var structure = Structure(UserResults.Render(
            [
                new JiraUser("Ada Lovelace", "ada", "ada@example.invalid", true),
                new JiraUser("A Departed Colleague", "adeparted", null, false),
            ],
            startAt: 0,
            maxResults: 25,
            includeInactive: true));

        // The display name is how a person disambiguates two similar people, and it is in the
        // prose. The email is personal data this server would be promising to carry stably.
        structure.ShouldBe(
            """
            {"outcome":"ok","startAt":0,"count":2,"includeInactive":true,"users":[{"username":"ada","active":true},{"username":"adeparted","active":false}]}
            """);

        structure.ShouldNotContain("Lovelace");
        structure.ShouldNotContain("example.invalid");
    }

    [Fact]
    public void A_user_search_that_matched_nothing_keeps_its_shape()
    {
        var structure = Structure(UserResults.Render(
            [],
            startAt: 50,
            maxResults: 25,
            includeInactive: false));

        // The shape does not appear and vanish with the result count, and the paging position is
        // still what the caller asked from.
        structure.ShouldBe(
            """
            {"outcome":"ok","startAt":50,"count":0,"includeInactive":false,"users":[]}
            """);
    }

    [Fact]
    public void The_account_carries_the_username_and_whether_it_is_active()
    {
        var structure = Structure(AccountDetail.Render(
            new JiraUser("Ada Lovelace", "ada", "ada@example.invalid", true),
            "work"));

        // The username here is the value most likely to be fed straight into an assignee field.
        structure.ShouldBe("""{"outcome":"ok","username":"ada","active":true}""");
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
                null,
                []),
            null);

    private static BulkIssueResult Failure(string key, Exception failure) =>
        new(key, null, failure);
}
