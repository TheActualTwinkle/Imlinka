using System.Diagnostics;

namespace Imlinka;

/// <summary>
/// Options for configuring trace span generation.
/// </summary>
public sealed class TracingRegistrationOptions
{
    /// <summary>
    /// If <c>true</c>, tracing spans will appear on all woven public methods,
    /// even without [Traced]/[Trace] attributes.
    /// </summary>
    public bool TraceAllPublicMethods { get; set; }

    /// <summary>
    /// Namespace prefixes to ignore when emitting spans.
    /// </summary>
    public ISet<string> IgnoredNamespacePrefixes { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Namespace prefixes that are allowed for tracing.
    /// If at least one prefix is present, ONLY matching namespaces are traced.
    /// Any [Traced]/[Trace] attributes on methods from other namespaces will be ignored.
    /// </summary>
    public ISet<string> TracedNamespacePrefixes { get; } = new HashSet<string>(StringComparer.Ordinal);

    internal ISet<string> AssemblyIdentities { get; } = new HashSet<string>(StringComparer.Ordinal);

    internal bool AssemblyFilterEnabled { get; set; }

    private bool _hasMergedOptions;

    /// <summary>
    /// ActivitySource of the generated spans.
    /// If not set, the default Imlinka ActivitySource is used.
    /// </summary>
    public ActivitySource? ActivitySource { get; private set; }

    /// <summary>
    /// Enables tracing for all woven public methods, even without [Traced]/[Trace] attributes.
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
    /// Adds an ActivitySource to be used for generated spans.
    /// If not set, the default Imlinka ActivitySource is used.
    /// </summary>
    /// <param name="activitySource">The ActivitySource to use for generated spans.</param>
    /// <returns>The <see cref="TracingRegistrationOptions"/> instance.</returns>
    public TracingRegistrationOptions WithActivitySource(ActivitySource activitySource)
    {
        ActivitySource = activitySource ??
                         throw new ArgumentNullException(nameof(activitySource));
        
        return this;
    }

    /// <summary>
    /// Adds namespace prefixes to trace.
    /// If configured, ONLY methods from these namespaces will be traced.
    /// Any [Traced]/[Trace] attributes on methods from other namespaces will be ignored.
    /// </summary>
    /// <param name="namespacePrefixes">Namespace prefixes to include into tracing candidates.</param>
    /// <returns>The <see cref="TracingRegistrationOptions"/> instance.</returns>
    public TracingRegistrationOptions WithTracedNamespacePrefixesOnly(IEnumerable<string> namespacePrefixes)
    {
        if (namespacePrefixes is null)
            throw new ArgumentNullException(nameof(namespacePrefixes));

        foreach (var prefix in namespacePrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            TracedNamespacePrefixes.Add(prefix);
        }

        return this;
    }

    internal void MergeFrom(TracingRegistrationOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        TraceAllPublicMethods |= options.TraceAllPublicMethods;

        if (options.ActivitySource is not null)
            ActivitySource = options.ActivitySource;

        if (!options.AssemblyFilterEnabled)
        {
            AssemblyFilterEnabled = false;
            AssemblyIdentities.Clear();
        }
        else if (!_hasMergedOptions || AssemblyFilterEnabled)
        {
            AssemblyFilterEnabled = true;
            AssemblyIdentities.UnionWith(options.AssemblyIdentities);
        }

        IgnoredNamespacePrefixes.UnionWith(options.IgnoredNamespacePrefixes);

        if (options.TracedNamespacePrefixes.Count > 0)
            TracedNamespacePrefixes.UnionWith(options.TracedNamespacePrefixes);

        _hasMergedOptions = true;
    }
}
