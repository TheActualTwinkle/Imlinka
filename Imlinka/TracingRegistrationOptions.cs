using System.Diagnostics;

namespace Imlinka;

/// <summary>
/// Options for configuring the behavior of trace spans gneration.
/// </summary>
public sealed class TracingRegistrationOptions
{
    /// <summary>
    /// If <c>true</c>, tracing spans will appear on all public methods of discovered services,
    /// even without [Traced]/[Trace] attributes.
    /// </summary>
    public bool TraceAllPublicMethods { get; set; }

    /// <summary>
    /// If <c>true</c>, original implementation registrations are kept in DI container
    /// in addition to traced interface proxies.
    /// </summary>
    public bool KeepOriginalImplementation { get; private set; }

    /// <summary>
    /// Namespace prefixes to ignore when scanning registered services.
    /// </summary>
    public ISet<string> IgnoredNamespacePrefixes { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Namespace prefixes that are allowed for tracing.
    /// If at least one prefix is present, only matching services are proxied.
    /// </summary>
    public ISet<string> ProxiedNamespacePrefixes { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// ActivitySource of the generated spans.
    /// If not set, spans will be created without a source, which may lead to missing spans in some tracing backends.
    /// </summary>
    public ActivitySource? ActivitySource { get; private set; }

    /// <summary>
    /// Enables tracing for all public methods of discovered services, even without [Traced]/[Trace] attributes.
    /// </summary>
    /// <returns>The <see cref="TracingRegistrationOptions"/> instance.</returns>
    public TracingRegistrationOptions WithPublicMethodsTracing()
    {
        TraceAllPublicMethods = true;
        
        return this;
    }

    /// <summary>
    /// Adds common framework namespaces to the ignore list, so services from those namespaces won't be traced.
    /// </summary>
    /// <returns>The <see cref="TracingRegistrationOptions"/> instance.</returns>
    public TracingRegistrationOptions IgnoreDefaultNamespaces()
    {
        IgnoredNamespacePrefixes.Add("Microsoft");
        IgnoredNamespacePrefixes.Add("System");
        
        return this;
    }

    /// <summary>
    /// Adds a ActivitySource to be used for generated spans.
    /// If not set, spans will be created without a source, which may lead to missing spans.
    /// </summary>
    /// <param name="activitySource">The ActivitySource to use for generated spans.</param>
    /// <returns>The <see cref="TracingRegistrationOptions"/> instance.</returns>
    public TracingRegistrationOptions WithActivitySource(ActivitySource activitySource)
    {
        ArgumentNullException.ThrowIfNull(activitySource);
        
        ActivitySource = activitySource;
        
        return this;
    }

    /// <summary>
    /// Adds namespace prefixes to proxy.
    /// If configured, only services from these namespaces will be proxied.
    /// </summary>
    /// <param name="namespacePrefixes">Namespace prefixes to include into tracing candidates.</param>
    /// <returns>The <see cref="TracingRegistrationOptions"/> instance.</returns>
    public TracingRegistrationOptions WithProxiedNamespacePrefixes(IEnumerable<string> namespacePrefixes)
    {
        ArgumentNullException.ThrowIfNull(namespacePrefixes);

        foreach (var prefix in namespacePrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            ProxiedNamespacePrefixes.Add(prefix);
        }

        return this;
    }

    /// <summary>
    /// Keeps original implementation registrations in DI container while replacing service interfaces with traced proxies.
    /// For example, <c>AddScoped&lt;IWorker, Worker&gt;()</c> keeps <c>Worker</c> resolvable and registers proxied <c>IWorker</c>.
    /// </summary>
    /// <returns>The <see cref="TracingRegistrationOptions"/> instance.</returns>
    public TracingRegistrationOptions KeepOriginalService()
    {
        KeepOriginalImplementation = true;

        return this;
    }
}