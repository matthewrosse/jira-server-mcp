namespace JiraServerMcp.Configuration;

/// <summary>
/// A configuration file this tool owns cannot be used, and the person at the terminal is the one
/// who can fix it. Carries the message they need to see: the dispatcher prints it and exits,
/// rather than letting a stack trace stand in for an explanation.
/// </summary>
internal sealed class ConfigurationException(string message) : Exception(message);
