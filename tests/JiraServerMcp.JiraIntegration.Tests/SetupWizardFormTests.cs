using JiraServerMcp.JiraIntegration.Tests.Harness;

namespace JiraServerMcp.JiraIntegration.Tests;

/// <summary>
/// The wizard driver's parser, against the pages a real 8.20.7 served during the Phase 0 spike.
/// No Jira and no Docker: these run on every pull request, unlike the rest of this project.
/// </summary>
public class SetupWizardFormTests
{
    private static string Page(string name) => File.ReadAllText(
        Path.Combine(
            RepositoryRoot.Find().FullName, "tests", "fixtures", "wizard", "8.20.7", name));

    [Fact]
    public void The_application_properties_page_yields_one_form_with_the_fields_jira_shipped()
    {
        var form = SetupWizardForm.SelectFrom(Page("1-application-properties.html"));

        form.Action.ShouldBe("SetupApplicationProperties.jspa");
        form.Fields.Keys.OrderBy(name => name)
            .ShouldBe(["atl_token", "baseURL", "mode", "nextStep", "title"]);
    }

    /// <summary>
    /// The trap the spike recorded: the licence page ships two forms posting to the same action.
    /// The first holds nothing but the cross-site request forgery token and posting it returns
    /// 500. A driver that takes the first match breaks here.
    /// </summary>
    [Fact]
    public void The_licence_page_yields_the_real_form_rather_than_the_stub_that_precedes_it()
    {
        var html = Page("2-license.html");

        SetupWizardForm.ParseAll(html).Count.ShouldBe(2);

        var form = SetupWizardForm.SelectFrom(html);

        form.Action.ShouldBe("SetupLicense.jspa");
        form.Fields.ShouldContainKey("setupLicenseKey");
    }

    [Fact]
    public void The_administrator_page_yields_the_account_fields()
    {
        var form = SetupWizardForm.SelectFrom(Page("3-admin-account.html"));

        form.Action.ShouldBe("SetupAdminAccount.jspa");
        form.Fields.Keys.OrderBy(name => name)
            .ShouldBe(["atl_token", "confirm", "email", "fullname", "password", "username"]);
    }

    /// <summary>
    /// The mail step ships seventeen inputs and two selects. They are carried through rather than
    /// enumerated, because a form posted without them fails validation and re-renders itself.
    /// </summary>
    [Fact]
    public void The_mail_page_carries_every_field_including_the_selects()
    {
        var form = SetupWizardForm.SelectFrom(Page("4-mail-notifications.html"));

        form.Action.ShouldBe("SetupMailNotifications.jspa");
        form.Fields.Count.ShouldBe(19);
        form.Fields.ShouldContainKey("noemail");
        form.Fields.ShouldContainKey("serviceProvider");
        form.Fields.ShouldContainKey("protocol");
    }

    /// <summary>
    /// Attributes arrive on their own lines in Jira's markup, so the parser cannot assume a tag
    /// sits on one line.
    /// </summary>
    [Fact]
    public void An_input_whose_attributes_span_several_lines_is_still_read()
    {
        var form = SetupWizardForm.SelectFrom(Page("1-application-properties.html"));

        form.Fields["atl_token"].ShouldBe(
            "SCRUBBED-XSRF-TOKEN-FIXTURE_0000000000000000000000000000000000000000_lout");
    }

    [Fact]
    public void A_submit_input_is_not_a_field_to_post_back()
    {
        var form = SetupWizardForm.SelectFrom("""
            <form action="Setup.jspa">
              <input type="hidden" name="atl_token" value="t"/>
              <input type="text" name="title" value="Jira"/>
              <input type="submit" name="next" value="Next"/>
            </form>
            """);

        form.Fields.Keys.OrderBy(name => name).ShouldBe(["atl_token", "title"]);
    }

    [Fact]
    public void A_select_contributes_its_selected_option()
    {
        var form = SetupWizardForm.SelectFrom("""
            <form action="Setup.jspa">
              <input type="hidden" name="atl_token" value="t"/>
              <select name="protocol">
                <option value="smtp">SMTP</option>
                <option value="smtps" selected="selected">SMTPS</option>
              </select>
            </form>
            """);

        form.Fields["protocol"].ShouldBe("smtps");
    }

    [Fact]
    public void A_page_with_no_form_is_reported_rather_than_guessed_at()
    {
        Should.Throw<InvalidOperationException>(
            () => SetupWizardForm.SelectFrom("<html><body>Jira is starting.</body></html>"));
    }
}
