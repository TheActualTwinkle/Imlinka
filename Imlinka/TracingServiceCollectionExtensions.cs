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
        /// Registers tracing proxies for eligible services from all registered service descriptors.
        /// </summary>
        /// <param name="configure">
        /// Configures <see cref="TracingRegistrationOptions"/> used to select services and tracing behavior.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public IServiceCollection AddProjectTracing(Action<TracingRegistrationOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            return services.AddProjectTracingInternal(configure);
        }

        /// <summary>
        /// Registers tracing proxies for eligible services whose interface or implementation belongs to the specified assembly.
        /// </summary>
        /// <param name="assembly">Assembly used to filter candidate services.</param>
        /// <param name="configure">
        /// Configures <see cref="TracingRegistrationOptions"/> used to select services and tracing behavior.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="assembly"/>, or <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public IServiceCollection AddProjectTracingForAssembly(
            Assembly assembly,
            Action<TracingRegistrationOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(configure);

            return services.AddProjectTracingInternal(configure, [assembly]);
        }
        
        /// <summary>
        /// Registers tracing proxies for eligible services whose interface or implementation belongs to any of the specified assemblies.
        /// </summary>
        /// <param name="assemblies">Assemblies used to filter candidate services.</param>
        /// <param name="configure">
        /// Configures <see cref="TracingRegistrationOptions"/> used to select services and tracing behavior.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="assemblies"/>, or <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public IServiceCollection AddProjectTracingForAssemblies(
            IEnumerable<Assembly> assemblies,
            Action<TracingRegistrationOptions> configure)
        {
            var assembliesList = assemblies as IReadOnlyCollection<Assembly> ?? assemblies.ToList();
            
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(assemblies);
            ArgumentNullException.ThrowIfNull(configure);

            return services.AddProjectTracingInternal(configure, assembliesList);
        }

        private IServiceCollection AddProjectTracingInternal(
            Action<TracingRegistrationOptions> configure,
            IReadOnlyCollection<Assembly>? assemblies = null)
        {
            ArgumentNullException.ThrowIfNull(configure);

            var options = new TracingRegistrationOptions();
            configure(options);

            var candidates = FindRegisteredCandidates(services, options, assemblies);

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

    private static List<RegisteredCandidate> FindRegisteredCandidates(
        IServiceCollection services,
        TracingRegistrationOptions options,
        IReadOnlyCollection<Assembly>? assemblies) =>
        services
            .Where(sd => sd.ImplementationType is not null)
            .Select(sd => new RegisteredCandidate(sd, sd.ServiceType, sd.ImplementationType!))
            .Where(c => c.ServiceType is { IsInterface: true, IsGenericTypeDefinition: false })
            .Where(c => c.ImplementationType is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(c => IsProjectType(c.ServiceType, options) && IsProjectType(c.ImplementationType, options))
            .Where(c => assemblies is null || assemblies.Contains(c.ServiceType.Assembly) || assemblies.Contains(c.ImplementationType.Assembly))
            .Where(c => options.TraceAllPublicMethods || IsMarkedForTracing(c.ServiceType, c.ImplementationType))
            .ToList();

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