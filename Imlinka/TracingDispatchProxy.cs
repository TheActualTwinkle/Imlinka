using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace Imlinka;

/// <summary>
/// Proxy that creates an Activity (span) around method calls when method/type tracing is enabled.
/// </summary>
internal class TracingDispatchProxy<T> : DispatchProxy where T : class
{
    private static readonly MethodInfo WrapTaskResultGenericMethod =
        typeof(TracingDispatchProxy<T>).GetMethod(
            nameof(WrapTaskResultGeneric),
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Cannot resolve task wrapper method.");

    private static readonly ConcurrentDictionary<
        (Type DecoratedType, MethodInfo InterfaceMethod, bool TraceAllPublicMethods),
        InvocationMetadata> InvocationCache = new();

    private readonly ConcurrentDictionary<Type, Func<Task, Activity, object>> _taskWrapperCache = new();

    private T? _decorated;
    private bool _traceAllPublicMethods;
    private ActivitySource? _activitySource;

    public void SetParameters(
        T decorated,
        bool traceAllPublicMethods = false,
        ActivitySource? activitySource = null)
    {
        _decorated = decorated;
        _traceAllPublicMethods = traceAllPublicMethods;
        _activitySource = activitySource;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (_decorated is null)
            throw new InvalidOperationException("Proxy isn't initialized.");

        var decoratedType = _decorated.GetType();
        var metadata = InvocationCache.GetOrAdd(
            (decoratedType, targetMethod, _traceAllPublicMethods),
            k => BuildInvocationMetadata(k.InterfaceMethod, k.DecoratedType, k.TraceAllPublicMethods));

        var source = _activitySource;
        var hasListeners = source is not null && metadata.IsTraced && source.HasListeners();
        Activity? activity = null;
        var callerActivity = Activity.Current;

        if (hasListeners)
        {
            activity = source!.StartActivity(metadata.SpanName!);
            if (activity is not null)
            {
                activity.SetTag("code.namespace", decoratedType.Namespace);
                activity.SetTag("code.function", metadata.ImplementationMethod.Name);
                activity.SetTag("code.type", decoratedType.Name);
            }
        }

        object? result;

        try
        {
            result = metadata.Invoker(_decorated, args ?? Array.Empty<object?>());
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            RecordException(activity, tie.InnerException);
            activity?.Dispose();
            throw tie.InnerException;
        }
        catch (Exception ex)
        {
            RecordException(activity, ex);
            activity?.Dispose();
            throw;
        }

        if (activity is null)
            return result;

        if (result is Task task)
        {
            Activity.Current = callerActivity;
            
            return WrapTaskResult(task, metadata.InterfaceMethod.ReturnType, activity);
        }

        activity.Dispose();
        return result;
    }

    private object WrapTaskResult(Task task, Type returnType, Activity activity)
    {
        if (returnType == typeof(Task))
            return WrapTask(task, activity);

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var wrapper = _taskWrapperCache.GetOrAdd(returnType, BuildTaskWrapper);
            return wrapper(task, activity);
        }

        return WrapTask(task, activity);
    }

    private static Func<Task, Activity, object> BuildTaskWrapper(Type returnType)
    {
        var resultType = returnType.GetGenericArguments()[0];
        var method = WrapTaskResultGenericMethod.MakeGenericMethod(resultType);
        return (Func<Task, Activity, object>)Delegate.CreateDelegate(typeof(Func<Task, Activity, object>), method);
    }

    private static async Task WrapTask(Task task, Activity activity)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RecordException(activity, ex);
            throw;
        }
        finally
        {
            activity.Dispose();
        }
    }

    private static object WrapTaskResultGeneric<TResult>(Task task, Activity activity)
        => WrapTaskResultCore((Task<TResult>)task, activity);

    private static async Task<TResult> WrapTaskResultCore<TResult>(Task<TResult> task, Activity activity)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RecordException(activity, ex);
            throw;
        }
        finally
        {
            activity.Dispose();
        }
    }

    private static InvocationMetadata BuildInvocationMetadata(
        MethodInfo interfaceMethod,
        Type decoratedType,
        bool traceAllPublicMethods)
    {
        var implementationMethod = TryResolveImplementationMethod(decoratedType, interfaceMethod) ?? interfaceMethod;
        var isTraced = traceAllPublicMethods || ShouldTrace(interfaceMethod, implementationMethod, decoratedType);
        var spanName = isTraced ? BuildSpanName(interfaceMethod, implementationMethod, decoratedType) : null;
        var invoker = BuildInvoker(implementationMethod);

        return new InvocationMetadata(interfaceMethod, implementationMethod, invoker, isTraced, spanName);
    }

    private static Func<object, object?[]?, object?> BuildInvoker(MethodInfo method)
    {
        if (method.GetParameters().Any(p => p.ParameterType.IsByRef))
            return (target, values) => method.Invoke(target, values);

        var targetParameter = Expression.Parameter(typeof(object), "target");
        var argsParameter = Expression.Parameter(typeof(object[]), "args");

        var callArguments = method
            .GetParameters()
            .Select((parameter, index) =>
                (Expression)Expression.Convert(
                    Expression.ArrayIndex(argsParameter, Expression.Constant(index)),
                    parameter.ParameterType))
            .ToArray();

        var instance = method.IsStatic
            ? null
            : Expression.Convert(targetParameter, method.DeclaringType!);

        var call = Expression.Call(instance, method, callArguments);
        Expression body = method.ReturnType == typeof(void)
            ? Expression.Block(call, Expression.Constant(null, typeof(object)))
            : Expression.Convert(call, typeof(object));

        var lambda = Expression.Lambda<Func<object, object?[]?, object?>>(
            body,
            targetParameter,
            argsParameter);

        return lambda.Compile();
    }

    private static string BuildSpanName(MethodInfo interfaceMethod, MethodInfo implMethod, Type decoratedType)
    {
        var trace = implMethod.GetCustomAttribute<TraceAttribute>(inherit: true)
                    ?? interfaceMethod.GetCustomAttribute<TraceAttribute>(inherit: true);

        if (!string.IsNullOrWhiteSpace(trace?.SpanName))
            return trace.SpanName;

        var traced = decoratedType.GetCustomAttribute<TracedAttribute>(inherit: true)
                     ?? decoratedType.GetInterfaces().FirstOrDefault(i => i == interfaceMethod.DeclaringType)
                         ?.GetCustomAttribute<TracedAttribute>(inherit: true);

        var prefix = traced?.SpanNamePrefix;
        var baseName = $"{decoratedType.Name}.{implMethod.Name}";
        return string.IsNullOrWhiteSpace(prefix) ? baseName : $"{prefix}.{baseName}";
    }

    private static bool ShouldTrace(MethodInfo interfaceMethod, MethodInfo implMethod, Type decoratedType)
    {
        if (interfaceMethod.GetCustomAttribute<TraceAttribute>(inherit: true) is not null)
            return true;

        if (implMethod.GetCustomAttribute<TraceAttribute>(inherit: true) is not null)
            return true;

        if (decoratedType.GetCustomAttribute<TracedAttribute>(inherit: true) is not null)
            return true;

        return interfaceMethod.DeclaringType?.GetCustomAttribute<TracedAttribute>(inherit: true) is not null;
    }

    private static MethodInfo? TryResolveImplementationMethod(Type decoratedType, MethodInfo interfaceMethod)
    {
        if (interfaceMethod.DeclaringType is null || !interfaceMethod.DeclaringType.IsInterface)
            return null;

        try
        {
            var map = decoratedType.GetInterfaceMap(interfaceMethod.DeclaringType);
            var index = Array.IndexOf(map.InterfaceMethods, interfaceMethod);
            if (index >= 0)
                return map.TargetMethods[index];
        }
        catch
        {
            // no-op
        }

        return null;
    }

    private static void RecordException(Activity? activity, Exception ex)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddException(ex);
    }

    private sealed record InvocationMetadata(
        MethodInfo InterfaceMethod,
        MethodInfo ImplementationMethod,
        Func<object, object?[]?, object?> Invoker,
        bool IsTraced,
        string? SpanName);
}