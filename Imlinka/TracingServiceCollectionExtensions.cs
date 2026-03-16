using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Imlinka;

/// <summary>
/// Extension methods for registering traced services in the DI container.
/// </summary>
public static class TracingServiceCollectionExtensions
{
    private static readonly MethodInfo CreateProxyGenericMethod =
        typeof(TracingServiceCollectionExtensions).GetMethod(
            nameof(CreateProxyGeneric),
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Cannot find proxy factory method.");

    private static readonly ConcurrentDictionary<Type, Func<object, bool, ActivitySource?, object>> ProxyFactoryCache = new();

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Bulk-registers traced services from a single assembly with filter options.
        /// </summary>
        public IServiceCollection AddProjectTracingForAssembly(
            Assembly assembly,
            Action<TracingRegistrationOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(configure);

            return services.AddProjectTracing(configure, [assembly]);
        }

        /// <summary>
        /// Bulk-registers traced services from the specified assemblies with filter options.
        /// </summary>
        public IServiceCollection AddProjectTracing(
            Action<TracingRegistrationOptions> configure,
            params Assembly[] assemblies)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);
            ArgumentOutOfRangeException.ThrowIfLessThan(assemblies.Length, 1);

            var options = new TracingRegistrationOptions();
            configure(options);

            var candidates = FindRegisteredCandidates(services, assemblies, options);

            foreach (var candidate in candidates)
            {
                RegisterTraced(
                    services,
                    candidate,
                    options.TraceAllPublicMethods,
                    options.ActivitySource);
            }

            return services;
        }
    }

    private static IReadOnlyList<RegisteredCandidate> FindRegisteredCandidates(
        IServiceCollection services,
        IReadOnlyCollection<Assembly> assemblies,
        TracingRegistrationOptions options)
    {
        return services
            .Where(sd => sd.ImplementationType is not null)
            .Select(sd => new RegisteredCandidate(sd, sd.ServiceType, sd.ImplementationType!))
            .Where(c => c.ServiceType is { IsInterface: true, IsGenericTypeDefinition: false })
            .Where(c => c.ImplementationType is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(c => IsProjectType(c.ServiceType, options) && IsProjectType(c.ImplementationType, options))
            .Where(c => assemblies.Contains(c.ServiceType.Assembly) || assemblies.Contains(c.ImplementationType.Assembly))
            .Where(c => options.TraceAllPublicMethods || IsMarkedForTracing(c.ServiceType, c.ImplementationType))
            .ToArray();
    }

    private static bool IsMarkedForTracing(Type interfaceType, Type implementationType)
    {
        if (interfaceType.GetCustomAttribute<TracedAttribute>(inherit: true) is not null)
            return true;

        if (implementationType.GetCustomAttribute<TracedAttribute>(inherit: true) is not null)
            return true;

        if (interfaceType.GetMethods().Any(m => m.GetCustomAttribute<TraceAttribute>(inherit: true) is not null))
            return true;

        return implementationType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Any(m => m.GetCustomAttribute<TraceAttribute>(inherit: true) is not null);
    }

    private static void RegisterTraced(
        IServiceCollection services,
        RegisteredCandidate candidate,
        bool traceAllPublicMethods,
        ActivitySource? activitySource)
    {
        services.Remove(candidate.Descriptor);

        services.TryAdd(ServiceDescriptor.Describe(
            candidate.ImplementationType,
            candidate.ImplementationType,
            candidate.Descriptor.Lifetime));

        services.Add(ServiceDescriptor.Describe(candidate.ServiceType, sp =>
        {
            var implementation = sp.GetRequiredService(candidate.ImplementationType);
            return CreateProxy(candidate.ServiceType, implementation, traceAllPublicMethods, activitySource);
        }, candidate.Descriptor.Lifetime));
    }

    private static object CreateProxy(
        Type interfaceType,
        object implementation,
        bool traceAllPublicMethods,
        ActivitySource? activitySource)
    {
        var proxyFactory = ProxyFactoryCache.GetOrAdd(interfaceType, BuildProxyFactory);
        return proxyFactory(implementation, traceAllPublicMethods, activitySource);
    }

    private static Func<object, bool, ActivitySource?, object> BuildProxyFactory(Type interfaceType)
    {
        var method = CreateProxyGenericMethod.MakeGenericMethod(interfaceType);
        return (Func<object, bool, ActivitySource?, object>)Delegate.CreateDelegate(
            typeof(Func<object, bool, ActivitySource?, object>),
            method);
    }

    private static object CreateProxyGeneric<TInterface>(
        object implementation,
        bool traceAllPublicMethods,
        ActivitySource? activitySource)
        where TInterface : class
    {
        var proxy = DispatchProxy.Create<TInterface, TracingDispatchProxy<TInterface>>();
        ((TracingDispatchProxy<TInterface>)(object)proxy)
            .SetParameters((TInterface)implementation, traceAllPublicMethods, activitySource);
        return proxy;
    }

    private static bool IsProjectType(Type type, TracingRegistrationOptions options)
    {
        var ns = type.Namespace;
        if (string.IsNullOrWhiteSpace(ns))
            return true;

        return !options.IgnoredNamespacePrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal));
    }

    private sealed record RegisteredCandidate(
        ServiceDescriptor Descriptor,
        Type ServiceType,
        Type ImplementationType);
}