using System.Reflection;
using JiraServerMcp.Tools;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// Pins the convention #45 set out to establish: every tool goes through <see cref="ToolCall"/>
/// rather than declaring its own failure ladder or its own Text/Error result helpers. A
/// seventeenth tool that grows its own copy fails this test rather than quietly drifting.
/// </summary>
public sealed class ToolCallConventionTests
{
    [Fact]
    public void No_tool_declares_its_own_result_helpers()
    {
        var offenders =
            from type in typeof(ToolCall).Assembly.GetTypes()
            where type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
            from member in type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            where member.Name is "Text" or "Error"
            select $"{type.Name}.{member.Name}";

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_tool_call_method_is_annotated_as_an_mcp_server_tool()
    {
        var toolTypes =
            from type in typeof(ToolCall).Assembly.GetTypes()
            where type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
            select type;

        toolTypes.Count().ShouldBe(21);
    }
}
