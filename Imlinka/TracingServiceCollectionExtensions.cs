using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Imlinka;

/// <summary>
/// Extension methods for configuring tracing of methods prepared by Imlinka IL weaving.
/// </summary>
public static class TracingServiceCollectionExtensions
{
    private static readonly ConditionalWeakTable<IServiceCollection, TracingRegistrationOptions> OptionsByServiceCollection = new();

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Configures tracing for methods prepared during build by Imlinka IL weaving.
        /// </summary>
        /// <param name="configure">
        /// Configures <see cref="TracingRegistrationOptions"/> used by woven methods at runtime.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public IServiceCollection AddProjectTracing(Action<TracingRegistrationOptions> configure)
        {
            if (services is null)
                throw new ArgumentNullException(nameof(services));

            if (configure is null)
                throw new ArgumentNullException(nameof(configure));

            var options = new TracingRegistrationOptions();
            configure(options);

            var accumulated = OptionsByServiceCollection.GetValue(services, _ => new TracingRegistrationOptions());
            lock (accumulated)
            {
                accumulated.MergeFrom(options);
                ProjectTracingRuntime.Configure(accumulated);
            }

            return services;
        }

        /// <summary>
        /// Configures tracing for methods prepared during build by Imlinka IL weaving.
        /// </summary>
        /// <param name="assembly">Assembly whose woven methods are allowed to emit spans.</param>
        /// <param name="configure">
        /// Configures <see cref="TracingRegistrationOptions"/> used by woven methods at runtime.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
        public IServiceCollection AddProjectTracingForAssembly(
            Assembly assembly,
            Action<TracingRegistrationOptions> configure)
        {
            if (assembly is null)
                throw new ArgumentNullException(nameof(assembly));

            if (configure is null)
                throw new ArgumentNullException(nameof(configure));

            return services.AddProjectTracing(options =>
            {
                options.AssemblyFilterEnabled = true;
                options.AssemblyIdentities.Add(assembly.FullName);
                configure(options);
            });
        }

        /// <summary>
        /// Configures tracing for methods prepared during build by Imlinka IL weaving.
        /// </summary>
        /// <param name="assemblies">Assemblies whose woven methods are allowed to emit spans.</param>
        /// <param name="configure">
        /// Configures <see cref="TracingRegistrationOptions"/> used by woven methods at runtime.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
        public IServiceCollection AddProjectTracingForAssemblies(
            IEnumerable<Assembly> assemblies,
            Action<TracingRegistrationOptions> configure)
        {
            if (assemblies is null)
                throw new ArgumentNullException(nameof(assemblies));

            if (configure is null)
                throw new ArgumentNullException(nameof(configure));

            return services.AddProjectTracing(options =>
            {
                options.AssemblyFilterEnabled = true;

                foreach (var assembly in assemblies)
                {
                    if (assembly is null)
                        throw new ArgumentException("Assembly collection cannot contain null values.", nameof(assemblies));

                    options.AssemblyIdentities.Add(assembly.FullName);
                }

                configure(options);
            });
        }
    }
}
