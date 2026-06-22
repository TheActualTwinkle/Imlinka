using System.Diagnostics;

namespace Imlinka;

/// <summary>
/// Runtime entry point used by build-time woven methods.
/// </summary>
public static class ProjectTracingRuntime
{
    private static readonly ActivitySource DefaultActivitySource = new("Imlinka");
    private static readonly object ConfigurationLock = new();

    private static volatile RuntimeConfiguration _configuration = RuntimeConfiguration.Default;

    /// <summary>
    /// Configures runtime tracing options used by methods prepared during build.
    /// </summary>
    /// <param name="options">Tracing options.</param>
    public static void Configure(TracingRegistrationOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        lock (ConfigurationLock)
        {
            _configuration = RuntimeConfiguration.Create(options);
        }
    }

    /// <summary>
    /// Starts a tracing scope for a method rewritten by the Imlinka build task.
    /// </summary>
    /// <param name="declaringType">Declaring type of the rewritten method.</param>
    /// <param name="methodName">Rewritten method name.</param>
    /// <param name="spanName">Explicit span name from <see cref="TraceAttribute" />.</param>
    /// <param name="spanNamePrefix">Span name prefix from <see cref="TracedAttribute" />.</param>
    /// <param name="tracedByAttribute">Whether the method was selected by a tracing attribute.</param>
    /// <returns>A disposable tracing scope.</returns>
    public static IDisposable StartScope(
        Type declaringType,
        string methodName,
        string? spanName,
        string? spanNamePrefix,
        bool tracedByAttribute)
    {
        if (declaringType is null)
            throw new ArgumentNullException(nameof(declaringType));

        if (methodName is null)
            throw new ArgumentNullException(nameof(methodName));

        var configuration = _configuration;

        if (!tracedByAttribute &&
            !configuration.TraceAllPublicMethods)
            return NullScope.Instance;

        if (!IsAssemblyEnabled(configuration, declaringType))
            return NullScope.Instance;

        if (!IsNamespaceEnabled(configuration, declaringType.Namespace))
            return NullScope.Instance;

        var source = configuration.ActivitySource ?? DefaultActivitySource;

        if (!source.HasListeners())
            return NullScope.Instance;

        var previous = Activity.Current;
        var activity = source.StartActivity(BuildSpanName(declaringType, methodName, spanName, spanNamePrefix));

        if (activity is null)
            return NullScope.Instance;

        activity.SetTag("code.namespace", declaringType.Namespace);
        activity.SetTag("code.function", methodName);
        activity.SetTag("code.type", declaringType.Name);

        return new ActivityScope(activity, previous);
    }

    private static bool IsNamespaceEnabled(RuntimeConfiguration configuration, string? ns)
    {
        ns ??= string.Empty;

        var proxiedPrefixes = configuration.TracedNamespacePrefixes;

        if (proxiedPrefixes.Length > 0 &&
            !proxiedPrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal)))
            return false;

        return !configuration.IgnoredNamespacePrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsAssemblyEnabled(RuntimeConfiguration configuration, Type declaringType)
    {
        return !configuration.AssemblyFilterEnabled ||
               configuration.AssemblyIdentities.Contains(declaringType.Assembly.FullName);
    }

    /// <summary>
    /// Records a synchronous exception and closes a tracing scope.
    /// </summary>
    /// <param name="scope">Tracing scope.</param>
    /// <param name="exception">Observed exception.</param>
    public static void FailScope(IDisposable scope, Exception exception)
    {
        if (scope is null)
            throw new ArgumentNullException(nameof(scope));

        if (exception is null)
            throw new ArgumentNullException(nameof(exception));

        if (scope is ActivityScope activityScope)
            activityScope.RecordException(exception);

        scope.Dispose();
    }

    /// <summary>
    /// Keeps a tracing scope open until a returned task completes.
    /// </summary>
    /// <param name="task">Returned task.</param>
    /// <param name="scope">Tracing scope.</param>
    /// <returns>The wrapped task.</returns>
    public static Task CompleteScope(Task task, IDisposable scope)
    {
        if (task is null)
            throw new ArgumentNullException(nameof(task));

        if (scope is null)
            throw new ArgumentNullException(nameof(scope));

        if (scope is ActivityScope activityScope)
            activityScope.ReleaseCurrent();

        return scope is NullScope ? task : CompleteTaskAsync(task, scope);
    }

    /// <summary>
    /// Keeps a tracing scope open until a returned task completes.
    /// </summary>
    /// <typeparam name="TResult">Task result type.</typeparam>
    /// <param name="task">Returned task.</param>
    /// <param name="scope">Tracing scope.</param>
    /// <returns>The wrapped task.</returns>
    public static Task<TResult> CompleteScope<TResult>(Task<TResult> task, IDisposable scope)
    {
        if (task is null)
            throw new ArgumentNullException(nameof(task));

        if (scope is null)
            throw new ArgumentNullException(nameof(scope));

        if (scope is ActivityScope activityScope)
            activityScope.ReleaseCurrent();

        return scope is NullScope ? task : CompleteTaskAsync(task, scope);
    }

    /// <summary>
    /// Keeps a tracing scope open until a returned value task completes.
    /// </summary>
    /// <param name="task">Returned value task.</param>
    /// <param name="scope">Tracing scope.</param>
    /// <returns>The wrapped value task.</returns>
    public static ValueTask CompleteScope(ValueTask task, IDisposable scope)
    {
        if (scope is null)
            throw new ArgumentNullException(nameof(scope));

        if (scope is ActivityScope activityScope)
            activityScope.ReleaseCurrent();

        return scope is NullScope ? task : CompleteValueTaskAsync(task, scope);
    }

    /// <summary>
    /// Keeps a tracing scope open until a returned value task completes.
    /// </summary>
    /// <typeparam name="TResult">ValueTask result type.</typeparam>
    /// <param name="task">Returned value task.</param>
    /// <param name="scope">Tracing scope.</param>
    /// <returns>The wrapped value task.</returns>
    public static ValueTask<TResult> CompleteScope<TResult>(ValueTask<TResult> task, IDisposable scope)
    {
        if (scope is null)
            throw new ArgumentNullException(nameof(scope));

        if (scope is ActivityScope activityScope)
            activityScope.ReleaseCurrent();

        return scope is NullScope ? task : CompleteValueTaskAsync(task, scope);
    }

    private static string BuildSpanName(Type declaringType, string methodName, string? spanName, string? spanNamePrefix)
    {
        if (!string.IsNullOrWhiteSpace(spanName))
            return spanName!;

        var baseName = $"{declaringType.Name}.{methodName}";

        return string.IsNullOrWhiteSpace(spanNamePrefix) ? baseName : $"{spanNamePrefix}.{baseName}";
    }

    private sealed class ActivityScope(Activity activity, Activity? previous) : IDisposable
    {
        private bool _currentReleased;

        public void ReleaseCurrent()
        {
            if (_currentReleased)
                return;

            if (ReferenceEquals(Activity.Current, activity))
                Activity.Current = previous;

            _currentReleased = true;
        }

        public void RecordException(Exception exception)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddException(exception);
        }

        public void Dispose()
        {
            ReleaseCurrent();
            activity.Dispose();
        }
    }

    private static async Task CompleteTaskAsync(Task task, IDisposable scope)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (scope is ActivityScope activityScope)
                activityScope.RecordException(ex);

            throw;
        }
        finally
        {
            scope.Dispose();
        }
    }

    private static async Task<TResult> CompleteTaskAsync<TResult>(Task<TResult> task, IDisposable scope)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (scope is ActivityScope activityScope)
                activityScope.RecordException(ex);

            throw;
        }
        finally
        {
            scope.Dispose();
        }
    }

    private static async ValueTask CompleteValueTaskAsync(ValueTask task, IDisposable scope)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (scope is ActivityScope activityScope)
                activityScope.RecordException(ex);

            throw;
        }
        finally
        {
            scope.Dispose();
        }
    }

    private static async ValueTask<TResult> CompleteValueTaskAsync<TResult>(ValueTask<TResult> task, IDisposable scope)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (scope is ActivityScope activityScope)
                activityScope.RecordException(ex);

            throw;
        }
        finally
        {
            scope.Dispose();
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class RuntimeConfiguration
    {
        public static readonly RuntimeConfiguration Default = new(
            traceAllPublicMethods: false,
            activitySource: null,
            assemblyFilterEnabled: false,
            assemblyIdentities: [],
            ignoredNamespacePrefixes: [],
            tracedNamespacePrefixes: []);

        private RuntimeConfiguration(
            bool traceAllPublicMethods,
            ActivitySource? activitySource,
            bool assemblyFilterEnabled,
            string[] assemblyIdentities,
            string[] ignoredNamespacePrefixes,
            string[] tracedNamespacePrefixes)
        {
            TraceAllPublicMethods = traceAllPublicMethods;
            ActivitySource = activitySource;
            AssemblyFilterEnabled = assemblyFilterEnabled;
            AssemblyIdentities = assemblyIdentities;
            IgnoredNamespacePrefixes = ignoredNamespacePrefixes;
            TracedNamespacePrefixes = tracedNamespacePrefixes;
        }

        public bool TraceAllPublicMethods { get; }

        public ActivitySource? ActivitySource { get; }

        public bool AssemblyFilterEnabled { get; }

        public string[] AssemblyIdentities { get; }

        public string[] IgnoredNamespacePrefixes { get; }

        public string[] TracedNamespacePrefixes { get; }

        public static RuntimeConfiguration Create(TracingRegistrationOptions options) =>
            new(
                options.TraceAllPublicMethods,
                options.ActivitySource,
                options.AssemblyFilterEnabled,
                options.AssemblyIdentities.ToArray(),
                options.IgnoredNamespacePrefixes.ToArray(),
                options.TracedNamespacePrefixes.ToArray());
    }
}
