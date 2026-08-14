using System.Diagnostics.CodeAnalysis;

namespace JiraServerMcp.Profiles;

/// <summary>
/// A base URL is validated once, at registration, so a typo fails while the user is looking at
/// the terminal rather than as a confusing tool error later. Plain HTTP is refused because the
/// personal access token is a bearer secret with nothing else protecting it in transit; the one
/// exception is a loopback address, where there is no network to intercept.
/// </summary>
internal static class ProfileUrl
{
    public static bool TryParse(
        string value,
        [NotNullWhen(true)] out Uri? baseUrl,
        [NotNullWhen(false)] out string? error)
    {
        baseUrl = null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate))
        {
            error = $"'{value}' is not an absolute URL. Give the full HTTPS base URL of your Jira "
                    + "Server, such as https://jira.example.com.";

            return false;
        }

        if (candidate.Scheme is not ("http" or "https"))
        {
            error = $"'{value}' is not an HTTP or HTTPS URL. The base URL must use HTTPS.";

            return false;
        }

        if (candidate.Scheme is "http" && !candidate.IsLoopback)
        {
            error = $"'{value}' is not an HTTPS URL. The base URL must use HTTPS, except for a "
                    + "loopback address such as http://localhost or http://127.0.0.1. There is "
                    + "no option to disable certificate validation.";

            return false;
        }

        baseUrl = candidate;
        error = null;

        return true;
    }
}
