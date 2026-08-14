using System.Text;

namespace JiraServerMcp.Cli;

/// <summary>
/// Reading a secret from a terminal without showing it. Keys are taken one at a time and never
/// written back, so nothing of the token reaches the screen, the scrollback, or a screen share.
/// </summary>
internal static class NoEchoPrompt
{
    /// <summary>
    /// Reads until Enter. Backspace removes the last character; control keys are ignored rather
    /// than stored, so an arrow key cannot end up inside a token.
    /// </summary>
    public static string Read(Func<ConsoleKeyInfo> readKey)
    {
        var secret = new StringBuilder();

        while (true)
        {
            var key = readKey();

            if (key.Key is ConsoleKey.Enter)
            {
                return secret.ToString();
            }

            if (key.Key is ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                {
                    secret.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
            }
        }
    }
}
