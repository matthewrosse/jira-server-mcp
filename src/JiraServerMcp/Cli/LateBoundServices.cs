namespace JiraServerMcp.Cli;

/// <summary>
/// The host's container, referred to before it exists. Tools must be registered on the builder, and
/// the container they resolve from is what the builder produces — so a tool built from a delegate
/// closes over this, and this is pointed at the container the moment there is one.
/// </summary>
/// <remarks>
/// Nothing resolves through it during startup: the only callers are tool invocations, which cannot
/// happen until the host is running. Asking earlier is a bug in this file, and it says so rather
/// than handing back a null.
/// </remarks>
internal sealed class LateBoundServices : IServiceProvider
{
    private IServiceProvider? _services;

    public IServiceProvider Bound
    {
        set => _services = value;
    }

    public object? GetService(Type serviceType) =>
        (_services ?? throw new InvalidOperationException(
            "A profile query asked for a service before the host was built."))
        .GetService(serviceType);
}
