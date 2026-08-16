using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;

namespace JiraServerMcp.JiraIntegration.Tests.Harness;

/// <summary>
/// One form off a first-run setup page: where it posts, and the fields Jira shipped in it.
/// </summary>
internal sealed record SetupWizardForm(string Action, IReadOnlyDictionary<string, string> Fields)
{
    /// <summary>
    /// The driver does not hard-code the wizard's steps. It reads whatever page Jira is showing,
    /// takes the form off it, and posts the fields back with the ones it has answers for
    /// overwritten — so a reordered wizard, or one with a step added, needs no change here.
    /// </summary>
    public static SetupWizardForm SelectFrom(string html) =>
        TrySelectStep(html, out var step)
            ? step
            : throw new InvalidOperationException(
                "No setup step on the page the wizard served. Jira is either not finished "
                + "starting or the wizard has changed shape; see tests/README.md.");

    /// <summary>
    /// True when the page is a wizard step. A configured Jira serves ordinary pages with ordinary
    /// forms on them, so only a form posting to a <c>Setup*</c> action counts — anything else, and
    /// the wizard is behind us.
    /// </summary>
    public static bool TrySelectStep(string html, [NotNullWhen(true)] out SetupWizardForm? step)
    {
        // The licence page carries two forms posting to SetupLicense.jspa: a stub holding
        // nothing but atl_token, whose post returns 500, and the real one carrying the key.
        // Whichever offers the most fields besides the token is the real one.
        step = ParseAll(html)
            .Where(form => form.Action.Contains("Setup", StringComparison.Ordinal))
            .MaxBy(form => form.Fields.Count(field => field.Key is not AtlassianTokenField));

        return step is not null;
    }

    public const string AtlassianTokenField = "atl_token";

    public static IReadOnlyList<SetupWizardForm> ParseAll(string html)
    {
        var forms = new List<SetupWizardForm>();

        foreach (Match block in _formPattern.Matches(html))
        {
            var action = _actionPattern.Match(block.Value);

            if (!action.Success)
            {
                continue;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Match tag in _inputPattern.Matches(block.Value))
            {
                var attributes = Attributes(tag.Value);

                // A submit button is a control, not state to carry back.
                if (!attributes.TryGetValue("name", out var name)
                    || string.Equals(attributes.GetValueOrDefault("type"), "submit",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fields[name] = Decode(attributes.GetValueOrDefault("value", string.Empty));
            }

            // The mail step's two selects matter: posted without them it fails validation and
            // re-renders the same page, which reads as a stuck wizard rather than a rejection.
            foreach (Match select in _selectPattern.Matches(block.Value))
            {
                var chosen = string.Empty;

                foreach (Match option in _optionPattern.Matches(select.Groups[2].Value))
                {
                    if (option.Groups[1].Value.Contains("selected", StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = Decode(Attributes(option.Value).GetValueOrDefault("value", string.Empty));
                        break;
                    }
                }

                fields[select.Groups[1].Value] = chosen;
            }

            forms.Add(new SetupWizardForm(Decode(action.Groups[1].Value), fields));
        }

        return forms;
    }

    private static Dictionary<string, string> Attributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match attribute in _attributePattern.Matches(tag))
        {
            attributes[attribute.Groups[1].Value] = attribute.Groups[2].Value;
        }

        return attributes;
    }

    /// <summary>
    /// Jira renders the base URL and the token into attribute values entity-encoded; posting
    /// them back encoded is a different value from the one it issued.
    /// </summary>
    private static string Decode(string value) => WebUtility.HtmlDecode(value);

    // Singleline so a tag whose attributes sit on their own lines — which is how Jira's
    // templates render them — is still one match.
    private static readonly RegexOptions _options =
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex _formPattern = new(@"<form\b[^>]*>.*?</form>", _options);

    private static readonly Regex _actionPattern = new(@"\baction\s*=\s*[""']([^""']*)[""']", _options);

    private static readonly Regex _inputPattern = new(@"<input\b[^>]*>", _options);

    private static readonly Regex _selectPattern =
        new(@"<select\b[^>]*\bname\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</select>", _options);

    private static readonly Regex _optionPattern = new(@"<option\b([^>]*)>", _options);

    private static readonly Regex _attributePattern =
        new(@"\b(name|value|type|selected)\s*=\s*[""']([^""']*)[""']", _options);
}
