using System.Reflection;
using JiraServerMcp.Jira;
using JiraServerMcp.Rendering;
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

    /// <summary>
    /// Paging policy — the floor, the clamp, the widened projection — is stated once, in
    /// <see cref="IssuePage"/> (#101). A tool that fetches a page of issues and does not route it
    /// through that module is a seventh copy of the policy. The client call itself stays in the
    /// tool, because the module takes a fetch rather than a JQL; what the tool may not do is fetch
    /// and then page for itself. Checked as a reference rather than as a grep for
    /// <c>Math.Clamp</c>, which is the brittle version.
    /// </summary>
    [Fact]
    public void No_tool_fetches_a_page_of_issues_without_going_through_the_issue_page_module()
    {
        var fetches = new[] { "SearchAsync", "GetBacklogAsync", "GetSprintIssuesAsync" };

        // ProfileQuerySurface builds its tools from a delegate rather than from an annotated type,
        // so it is named here or it escapes the net the attribute casts.
        var pagingTypes =
            from type in typeof(ToolCall).Assembly.GetTypes()
            where type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
                  || type == typeof(ProfileQuerySurface)
            select type;

        var fetching =
            (from type in pagingTypes
             let calls = Calls(type).ToArray()
             where calls.Any(called =>
                 called.DeclaringType == typeof(JiraClient)
                 && fetches.Contains(called.Name, StringComparer.Ordinal))
             select (Type: type, Calls: calls)).ToArray();

        // Six types: the five annotated tool types that page issues, plus ProfileQuerySurface,
        // which is one type behind however many tools an operator declared. Asserted so that a
        // walk which stopped finding calls at all — a nested type the compiler moved, an opcode it
        // stopped emitting — fails here rather than passing the check below vacuously.
        fetching.Length.ShouldBe(6);

        var offenders =
            from candidate in fetching
            where !candidate.Calls.Any(called =>
                called.DeclaringType == typeof(IssuePage)
                && called.Name is nameof(IssuePage.RunAsync))
            select candidate.Type.Name;

        offenders.Distinct().ShouldBeEmpty();
    }

    /// <summary>
    /// The idempotency key's policy — claim before sending, replay the three endings, record the
    /// ending — is stated once, in <see cref="RetrySafeWrite"/> (#102). A tool that reaches past
    /// that module to <see cref="WriteAttempts"/> is a fourth copy of the ordering the whole
    /// feature rests on. Checked as a reference rather than as a grep, as the paging case is.
    /// </summary>
    [Fact]
    public void No_tool_claims_or_sends_a_keyed_write_without_going_through_the_retry_safe_module()
    {
        var reserved = new[] { nameof(WriteAttempts.TryBegin), nameof(WriteAttempts.SendAsync) };

        var offenders =
            from type in typeof(ToolCall).Assembly.GetTypes()
            where type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
            from called in Calls(type)
            where called.DeclaringType == typeof(WriteAttempts)
                  && reserved.Contains(called.Name, StringComparer.Ordinal)
            select $"{type.Name}.{called.Name}";

        offenders.Distinct().ShouldBeEmpty();
    }

    /// <summary>
    /// The check above passes vacuously if the walk stops seeing the calls at all, so the module
    /// that is allowed to make them is asserted to still make both.
    /// </summary>
    [Fact]
    public void The_retry_safe_module_is_what_claims_the_key_and_sends_the_write()
    {
        var called =
            (from method in Calls(typeof(RetrySafeWrite))
             where method.DeclaringType == typeof(WriteAttempts)
             select method.Name).Distinct();

        called.ShouldBe(
            [nameof(WriteAttempts.TryBegin), nameof(WriteAttempts.SendAsync)],
            ignoreOrder: true);
    }

    /// <summary>
    /// A tool frames untrusted content one way: <see cref="UntrustedContent.Envelope"/>. Two of
    /// them used to lay out the preamble and the markers by hand (#103), which is two places
    /// the marker order can drift from what <see cref="UntrustedContent"/> intends. Read off the
    /// source rather than off the IL, because <see cref="UntrustedContent.Preamble"/> is a
    /// constant and the compiler leaves no call behind for the walk above to find.
    /// </summary>
    [Fact]
    public void No_tool_frames_untrusted_content_by_hand()
    {
        var offenders =
            from file in new DirectoryInfo(
                    Path.Combine(RepositoryRoot.Find().FullName, "src", "JiraServerMcp", "Tools"))
                .GetFiles("*.cs", SearchOption.AllDirectories)
            let source = File.ReadAllText(file.FullName)
            where source.Contains("UntrustedContent.Preamble", StringComparison.Ordinal)
                  || source.Contains("UntrustedContent.Delimit", StringComparison.Ordinal)
            select file.Name;

        offenders.ShouldBeEmpty();
    }

    /// <summary>Every method a type's own code calls, read off its compiled bodies.</summary>
    private static IEnumerable<MethodBase> Calls(Type type) =>
        from nested in WithNestedTypes(type)
        from method in nested.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        from called in MethodCalls(method)
        select called;

    /// <summary>
    /// A type and everything the compiler nested inside it. A lambda that captures becomes a
    /// display class, and an async lambda's state machine is nested inside that — so the walk
    /// recurses rather than taking one level.
    /// </summary>
    private static IEnumerable<Type> WithNestedTypes(Type type) =>
        type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(WithNestedTypes)
            .Prepend(type);

    private static IEnumerable<MethodBase> MethodCalls(MethodBase method)
    {
        if (method.GetMethodBody() is not { } body)
        {
            yield break;
        }

        var module = method.Module;
        var il = body.GetILAsByteArray() ?? [];

        for (var offset = 0; offset < il.Length - 4; offset++)
        {
            // call (0x28) and callvirt (0x6F), each followed by a four-byte metadata token.
            if (il[offset] is not (0x28 or 0x6F))
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, offset + 1);
            MethodBase? called = null;

            try
            {
                called = module.ResolveMethod(
                    token,
                    method.DeclaringType?.GetGenericArguments(),
                    method.GetGenericArguments());
            }
            catch (ArgumentException)
            {
                // A token that is not a method reference: this walk is a scan rather than a
                // decoder, so an operand that happens to sit where one is expected is skipped.
            }

            if (called is not null)
            {
                yield return called;
            }
        }
    }

    [Fact]
    public void Every_tool_call_method_is_annotated_as_an_mcp_server_tool()
    {
        var toolTypes =
            from type in typeof(ToolCall).Assembly.GetTypes()
            where type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
            select type;

        toolTypes.Count().ShouldBe(24);
    }
}
