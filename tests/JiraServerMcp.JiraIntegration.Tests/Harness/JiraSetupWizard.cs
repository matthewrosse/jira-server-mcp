using System.Net;
using System.Text.RegularExpressions;

namespace JiraServerMcp.JiraIntegration.Tests.Harness;

/// <summary>
/// Drives Jira's first-run setup over HTTP, so no human touches a browser. Ported from the Phase 0
/// spike's <c>scripts/phase0/02-setup.py</c>, which is what proved the sequence; see
/// <c>tests/README.md</c> for the requests it makes and the traps they cover.
/// </summary>
internal sealed class JiraSetupWizard(Uri baseUrl, JiraAdministrator administrator, string licenseKey)
{
    /// <summary>
    /// The wizard terminates here, not at the dashboard — the dashboard only follows once a human
    /// clicks through.
    /// </summary>
    private static readonly string[] _terminals =
        ["/secure/WelcomeToJIRA.jspa", "/secure/Dashboard.jspa", "/login.jsp"];

    /// <summary>
    /// A wizard with a step added still finishes; this only bounds a driver going in circles.
    /// </summary>
    private const int MostSteps = 15;

    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var cookies = new CookieContainer();

        using var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = true,
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = baseUrl,
            Timeout = TimeSpan.FromMinutes(5),
        };

        var (page, landedOn) = await GetAsync(client, baseUrl, cancellationToken);

        for (var step = 1; step <= MostSteps; step++)
        {
            if (_terminals.Any(terminal => landedOn.AbsolutePath.Contains(terminal, StringComparison.Ordinal)))
            {
                // A re-run against an instance somebody already set up lands here on the first
                // fetch, which is not a failure: the instance is simply already configured.
                return step > 1;
            }

            // A page that is not a wizard step means the wizard is behind us. Jira does not
            // always finish on one of the pages above: it can land straight on an ordinary page,
            // and a driver that keeps looking for "some form" finds the site's quick search and
            // posts that to itself until it runs out of steps.
            if (!SetupWizardForm.TrySelectStep(page, out var form))
            {
                return step > 1;
            }

            var target = new Uri(landedOn, form.Action);

            (page, landedOn) = await PostAsync(
                client, target, Fill(form, cookies), landedOn, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Jira's setup wizard did not finish in {MostSteps} steps; it is stuck at {landedOn}. "
            + "See \"When the wizard changes shape\" in tests/README.md.");
    }

    /// <summary>
    /// Overwrites the fields there are answers for and carries the rest back as Jira sent them.
    /// The mail step ships nineteen fields, and one posted without them re-renders itself.
    /// </summary>
    private IReadOnlyDictionary<string, string> Fill(SetupWizardForm form, CookieContainer cookies)
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = "Harness Jira",
            ["mode"] = "private",
            ["baseURL"] = baseUrl.ToString().TrimEnd('/'),
            ["setupLicenseKey"] = licenseKey,
            ["licenseKey"] = licenseKey,
            ["username"] = administrator.Username,
            ["fullname"] = administrator.FullName,
            ["email"] = administrator.Email,
            ["password"] = administrator.Password,
            ["confirm"] = administrator.Password,
            ["noemail"] = "true",
        };

        var filled = form.Fields.ToDictionary(
            field => field.Key,
            field => answers.GetValueOrDefault(field.Key, field.Value),
            StringComparer.Ordinal);

        // Jira binds atl_token to the session, so the value that works is the one in the
        // atlassian.xsrf.token cookie rather than the one embedded in the form. Post the form's
        // and every step returns 403.
        var token = cookies.GetCookies(baseUrl)
            .FirstOrDefault(cookie => cookie.Name is "atlassian.xsrf.token")?.Value;

        if (token is not null)
        {
            filled[SetupWizardForm.AtlassianTokenField] = token;
        }

        return filled;
    }

    private static async Task<(string Page, Uri LandedOn)> GetAsync(
        HttpClient client, Uri url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);

        return (await response.Content.ReadAsStringAsync(cancellationToken),
            response.RequestMessage?.RequestUri ?? url);
    }

    private static async Task<(string Page, Uri LandedOn)> PostAsync(
        HttpClient client,
        Uri url,
        IReadOnlyDictionary<string, string> fields,
        Uri referer,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(fields),
        };

        // The cross-site request forgery filter rejects a post whose Referer is not the instance.
        request.Headers.Referrer = referer;

        using var response = await client.SendAsync(request, cancellationToken);

        var page = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Jira's setup wizard answered {(int)response.StatusCode} to {url}. "
                + $"Page errors: {string.Join("; ", Errors(page))}");
        }

        return (page, response.RequestMessage?.RequestUri ?? url);
    }

    /// <summary>
    /// A step that re-renders itself rather than advancing was rejected, and the reason is on the
    /// page. Surfacing it beats a driver that silently loops until it runs out of steps.
    /// </summary>
    private static IReadOnlyList<string> Errors(string page)
    {
        var errors = new List<string>();

        foreach (Match match in _errorPattern.Matches(page))
        {
            var text = Regex.Replace(match.Groups[1].Value, "<[^>]+>", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (text.Length > 0)
            {
                errors.Add(text);
            }
        }

        return errors.Count > 0 ? errors : ["none reported"];
    }

    /// <summary>
    /// The two shapes Jira renders wizard validation failures in.
    /// </summary>
    private static readonly Regex _errorPattern = new(
        """<(?:div|span)[^>]*class="[^"]*(?:aui-message[^"]*error|errMsg)[^"]*"[^>]*>(.*?)</(?:div|span)>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
