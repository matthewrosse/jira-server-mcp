using JiraServerMcp.Cli;

namespace JiraServerMcp.Tests;

/// <summary>
/// The terminal prompt reads keys and writes nothing back, which is what "echo off" amounts to.
/// Whether the keys arrive intercepted is the caller's business, and is asserted nowhere: a
/// terminal cannot be faked from a test process without a pseudo-terminal.
/// </summary>
public sealed class NoEchoPromptTests
{
    [Fact]
    public void Enter_ends_the_token()
    {
        Read("pat", ConsoleKey.Enter).ShouldBe("pat");
    }

    [Fact]
    public void Backspace_removes_the_last_character()
    {
        var keys = new Queue<ConsoleKeyInfo>(
        [
            Key('p'), Key('a'), Key('x'),
            new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
            Key('t'),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        ]);

        NoEchoPrompt.Read(keys.Dequeue).ShouldBe("pat");
    }

    [Fact]
    public void Backspace_on_an_empty_token_is_harmless()
    {
        var keys = new Queue<ConsoleKeyInfo>(
        [
            new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
            Key('p'),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        ]);

        NoEchoPrompt.Read(keys.Dequeue).ShouldBe("p");
    }

    [Fact]
    public void A_control_key_does_not_land_inside_the_token()
    {
        var keys = new Queue<ConsoleKeyInfo>(
        [
            Key('p'),
            new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false),
            Key('t'),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        ]);

        NoEchoPrompt.Read(keys.Dequeue).ShouldBe("pt");
    }

    private static string Read(string typed, ConsoleKey terminator)
    {
        var keys = new Queue<ConsoleKeyInfo>(typed.Select(Key));

        keys.Enqueue(new ConsoleKeyInfo('\r', terminator, false, false, false));

        return NoEchoPrompt.Read(keys.Dequeue);
    }

    private static ConsoleKeyInfo Key(char character) =>
        new(character, ConsoleKey.None, false, false, false);
}
