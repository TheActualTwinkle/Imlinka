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
                    options.ActivitySource,
                    options.KeepOriginalImplementation);
            }

            return services;
        }
    }

    private static List<RegisteredCandidate> FindRegisteredCandidates(
        IServiceCollection services,
        TracingRegistrationOptions options,
        IReadOnlyCollection<Assembly>? assemblies) =>
        services
            .Select(CreateRegisteredCandidate)
            .Where(c => c is not null)
            .Select(c => c!)
            .Where(c => c.ServiceType is { IsInterface: true, IsGenericTypeDefinition: false })
            .Where(c => c.ImplementationType is null or { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(c => IsDispatchProxyCompatible(c.ServiceType))
            .Where(c => IsProjectType(c.ServiceType, options))
            .Where(c => c.ImplementationType is null || IsProjectType(c.ImplementationType, options))
            .Where(c => assemblies is null || assemblies.Contains(c.ServiceType.Assembly) || (c.ImplementationType is not null && assemblies.Contains(c.ImplementationType.Assembly)))
            .Where(c => options.TraceAllPublicMethods || IsMarkedForTracing(c.ServiceType, c.ImplementationType))
            .ToList();

    private static RegisteredCandidate? CreateRegisteredCandidate(ServiceDescriptor descriptor)
    {
        if (!TryGetImplementation(descriptor, out var implementationType, out var implementationFactory, out var keyedImplementationFactory, out var implementationInstance))
            return null;

        return new RegisteredCandidate(
            descriptor,
            descriptor.ServiceType,
            implementationType,
            descriptor.ServiceKey,
            descriptor.Lifetime,
            implementationFactory,
            keyedImplementationFactory,
            implementationInstance);
    }

    private static bool TryGetImplementation(
        ServiceDescriptor descriptor,
        out Type? implementationType,
        out Func<IServiceProvider, object>? implementationFactory,
        out Func<IServiceProvider, object?, object>? keyedImplementationFactory,
        out object? implementationInstance)
    {
        implementationType = null;
        implementationFactory = null;
        keyedImplementationFactory = null;
        implementationInstance = null;

        if (descriptor.IsKeyedService)
        {
            implementationType = descriptor.KeyedImplementationType;
            keyedImplementationFactory = descriptor.KeyedImplementationFactory;
            implementationInstance = descriptor.KeyedImplementationInstance;

            return implementationType is not null || keyedImplementationFactory is not null || implementationInstance is not null;
        }

        implementationType = descriptor.ImplementationType;
        implementationFactory = descriptor.ImplementationFactory;
        implementationInstance = descriptor.ImplementationInstance;

        return implementationType is not null || implementationFactory is not null || implementationInstance is not null;
    }

    private static bool IsDispatchProxyCompatible(Type interfaceType) =>
        interfaceType
            .GetMethods()
            .All(IsDispatchProxyCompatibleMethod);

    private static bool IsDispatchProxyCompatibleMethod(MethodInfo method)
    {
        if (ContainsByRefLikeType(method.ReturnType))
            return false;

        return method
            .GetParameters()
            .All(parameter => !ContainsByRefLikeType(parameter.ParameterType));
    }

    private static bool ContainsByRefLikeType(Type type)
    {
        while (true)
        {
            if (type.IsByRef || type.IsPointer)
                type = type.GetElementType() ?? type;

            if (type.IsByRefLike)
                return true;

            if (type.IsArray)
            {
                type = type.GetElementType()!;

                continue;
            }

            if (!type.IsGenericType)
                return false;

            return type.GetGenericArguments()
                .Any(ContainsByRefLikeType);
        }
    }

    private static bool IsMarkedForTracing(Type interfaceType, Type? implementationType)
    {
        if (IsInterfaceMarkedForTracing(interfaceType))
            return true;

        if (implementationType is null)
            return false;

        if (implementationType.GetCustomAttribute<TracedAttribute>(inherit: true) is not null)
            return true;

        return implementationType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Any(m => m.GetCustomAttribute<TraceAttribute>(inherit: true) is not null);
    }

    private static bool IsInterfaceMarkedForTracing(Type interfaceType)
    {
        if (interfaceType.GetCustomAttribute<TracedAttribute>(inherit: true) is not null)
            return true;

        return interfaceType.GetMethods().Any(m => m.GetCustomAttribute<TraceAttribute>(inherit: true) is not null);
    }

    private static void RegisterTraced(
        IServiceCollection services,
        RegisteredCandidate candidate,
        bool traceAllPublicMethods,
        ActivitySource? activitySource,
        bool keepOriginalImplementation)
    {
        services.Remove(candidate.Descriptor);

        object? implementationServiceKey;

        if (!keepOriginalImplementation && !candidate.IsKeyedService)
            implementationServiceKey = null;
        else
            implementationServiceKey = ResolveImplementationServiceKey(candidate, keepOriginalImplementation);

        // When KeepOriginalService() is enabled, ensure the concrete implementation remains resolvable.
        // This must work for ImplementationType registrations AND for factory/instance-based registrations.
        if (keepOriginalImplementation && implementationServiceKey is not null)
        {
            var implementationType = candidate.ImplementationType ?? TryInferImplementationType(candidate.ServiceType);

            if (implementationType is not null)
            {
                var implementationDescriptor = CreateKeptImplementationDescriptor(
                    implementationType,
                    candidate,
                    candidate.Lifetime,
                    implementationServiceKey);

                services.TryAdd(implementationDescriptor);
            }
        }
        else if (candidate.ImplementationType is not null && implementationServiceKey is not null)
        {
            // If we are not keeping original implementation, we may use the concrete type as a
            // separate registration for keyed services to ensure proxy resolves the correct lifetime.
            var descriptor = CreateImplementationDescriptor(candidate.ImplementationType, candidate.Lifetime, implementationServiceKey);
            services.Add(descriptor);
        }

        services.Add(CreateDescriptor(candidate.ServiceType, candidate, (sp, serviceKey) =>
        {
            var implementation = ResolveImplementation(sp, candidate, serviceKey, implementationServiceKey);
            return CreateProxy(candidate.ServiceType, implementation, traceAllPublicMethods, activitySource);
        }));
    }

    private static Type? TryInferImplementationType(Type serviceType)
    {
        // For factory/instance registrations, ImplementationType is null.
        // Still, for KeepOriginalService we want to expose the concrete class if it's a typical "IService -> Service" mapping.
        if (!serviceType.IsInterface)
            return null;

        var ns = serviceType.Namespace;
        if (string.IsNullOrWhiteSpace(ns))
            return null;

        var interfaceName = serviceType.Name;
        if (interfaceName.Length < 2 || interfaceName[0] != 'I')
            return null;

        var implementationName = interfaceName[1..];
        var fullName = $"{ns}.{implementationName}";

        return serviceType.Assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
    }

    private static ServiceDescriptor CreateKeptImplementationDescriptor(
        Type implementationType,
        RegisteredCandidate candidate,
        ServiceLifetime lifetime,
        object? implementationServiceKey)
    {
        if (candidate.ImplementationInstance is not null)
        {
            return implementationServiceKey is Unkeyed
                ? ServiceDescriptor.Describe(implementationType, _ => candidate.ImplementationInstance, lifetime)
                : ServiceDescriptor.DescribeKeyed(implementationType, implementationServiceKey, (_, _) => candidate.ImplementationInstance, lifetime);
        }

        if (candidate is { IsKeyedService: true, KeyedImplementationFactory: not null })
        {
            return implementationServiceKey is Unkeyed
                ? ServiceDescriptor.Describe(implementationType, sp => candidate.KeyedImplementationFactory(sp, candidate.ServiceKey), lifetime)
                : ServiceDescriptor.DescribeKeyed(implementationType, implementationServiceKey, (sp, _) => candidate.KeyedImplementationFactory(sp, candidate.ServiceKey), lifetime);
        }

        if (candidate is { IsKeyedService: false, ImplementationFactory: not null })
        {
            return implementationServiceKey is Unkeyed
                ? ServiceDescriptor.Describe(implementationType, sp => candidate.ImplementationFactory(sp), lifetime)
                : ServiceDescriptor.DescribeKeyed(implementationType, implementationServiceKey, (sp, _) => candidate.ImplementationFactory(sp), lifetime);
        }

        // Fallback for ImplementationType registrations.
        return CreateImplementationDescriptor(implementationType, lifetime, implementationServiceKey);
    }

    private static object? ResolveImplementationServiceKey(RegisteredCandidate candidate, bool keepOriginalImplementation)
    {
        if (keepOriginalImplementation)
            return candidate.IsKeyedService ? candidate.ServiceKey : new Unkeyed();

        return candidate.ServiceKey;
    }

    private static object ResolveImplementation(
        IServiceProvider serviceProvider,
        RegisteredCandidate candidate,
        object? serviceKey,
        object? implementationServiceKey)
    {
        // Only when KeepOriginalService() is enabled for UNKEYED services we pass an Unkeyed,
        // and only then we can safely resolve through the kept concrete registration.
        // For keyed services without KeepOriginalService(), implementationServiceKey equals the key and
        // there is NO kept concrete registration to resolve.
        if (implementationServiceKey is Unkeyed)
        {
            var inferred = candidate.ImplementationType ?? TryInferImplementationType(candidate.ServiceType);
            if (inferred is not null)
                return serviceProvider.GetRequiredService(inferred);
        }

        // KeepOriginalService() for keyed services keeps the concrete keyed registration under the same key.
        // For factory/instance registrations, candidate.ImplementationType is null, so we must resolve via the inferred type.
        // Important: we must NOT do this when KeepOriginalService() is disabled (there is no kept concrete registration).
        if (candidate.IsKeyedService
            && implementationServiceKey is not null
            && implementationServiceKey is not Unkeyed
            && candidate.ImplementationType is null
            && (candidate.KeyedImplementationFactory is not null || candidate.ImplementationInstance is not null)
            && TryInferImplementationType(candidate.ServiceType) is { } inferredKeyedImpl)
        {
            // This path is only valid when KeepOriginalService() was enabled, because that's the only time we add
            // a keyed concrete registration for factory/instance descriptors.
            // When KeepOriginalService() is disabled, the following resolution would throw, so we fall back to invoking the factory.
            try
            {
                return serviceProvider.GetRequiredKeyedService(inferredKeyedImpl, implementationServiceKey);
            }
            catch (InvalidOperationException)
            {
                // Fall through to original factory/instance resolution.
            }
        }

        // KeepOriginalService() for keyed services with a known implementation type.
        // However, without KeepOriginalService(), implementationServiceKey is also non-null (it's just the key)
        // and we must NOT try to resolve a concrete keyed implementation type that was never registered.
        if (candidate.IsKeyedService
            && implementationServiceKey is not null
            && implementationServiceKey is not Unkeyed
            && candidate.ImplementationType is not null
            && candidate.Descriptor.IsKeyedService
            && candidate.Descriptor.KeyedImplementationType is not null)
        {
            // Only safe when the original descriptor had a known keyed implementation type.
            return serviceProvider.GetRequiredKeyedService(candidate.ImplementationType, implementationServiceKey);
        }

        if (candidate.ImplementationType is not null)
        {
            if (implementationServiceKey is null)
                return ActivatorUtilities.CreateInstance(serviceProvider, candidate.ImplementationType);

            if (implementationServiceKey is Unkeyed)
                return serviceProvider.GetRequiredService(candidate.ImplementationType);

            return serviceProvider.GetRequiredKeyedService(candidate.ImplementationType, implementationServiceKey);
        }

        if (candidate.IsKeyedService && candidate.KeyedImplementationFactory is not null)
            return candidate.KeyedImplementationFactory(serviceProvider, implementationServiceKey ?? serviceKey);

        if (!candidate.IsKeyedService && candidate.ImplementationFactory is not null)
            return candidate.ImplementationFactory(serviceProvider);

        if (candidate.ImplementationInstance is not null)
            return candidate.ImplementationInstance;

        throw new InvalidOperationException($"Cannot resolve implementation for service '{candidate.ServiceType}'.");
    }

    private static ServiceDescriptor CreateImplementationDescriptor(
        Type implementationType,
        ServiceLifetime lifetime,
        object? serviceKey) =>
        serviceKey is Unkeyed
            ? ServiceDescriptor.Describe(implementationType, implementationType, lifetime)
            : ServiceDescriptor.DescribeKeyed(implementationType, serviceKey, implementationType, lifetime);

    private static ServiceDescriptor CreateDescriptor(
        Type serviceType,
        RegisteredCandidate candidate,
        Func<IServiceProvider, object?, object>? keyedFactory = null)
    {
        if (!candidate.IsKeyedService)
            return keyedFactory is null
                ? ServiceDescriptor.Describe(serviceType, serviceType, candidate.Lifetime)
                : ServiceDescriptor.Describe(serviceType, sp => keyedFactory(sp, null), candidate.Lifetime);

        return keyedFactory is null
            ? ServiceDescriptor.DescribeKeyed(serviceType, candidate.ServiceKey, serviceType, candidate.Lifetime)
            : ServiceDescriptor.DescribeKeyed(serviceType, candidate.ServiceKey, keyedFactory, candidate.Lifetime);
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
            return options.ProxiedNamespacePrefixes.Count == 0;

        var isIncluded = options.ProxiedNamespacePrefixes.Count == 0
            || options.ProxiedNamespacePrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal));

        if (!isIncluded)
            return false;

        return !options.IgnoredNamespacePrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal));
    }

    private sealed record RegisteredCandidate(
        ServiceDescriptor Descriptor,
        Type ServiceType,
        Type? ImplementationType,
        object? ServiceKey,
        ServiceLifetime Lifetime,
        Func<IServiceProvider, object>? ImplementationFactory,
        Func<IServiceProvider, object?, object>? KeyedImplementationFactory,
        object? ImplementationInstance)
    {
        public bool IsKeyedService => ServiceKey is not null || Descriptor.IsKeyedService;
    }

    private sealed class Unkeyed;
}