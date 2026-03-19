using FluentAssertions;
using Imlinka.Tests.TestModels.NotProxied;
using Imlinka.Tests.TestModels.Proxied;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Verifies namespace prefix filtering for tracing candidates.
/// </summary>
public sealed class TracingServiceCollectionNamespaceFilteringTests
{
    /// <summary>
    /// With namespace allowlist only matching namespace services should be proxied.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenProxiedNamespacePrefixesConfigured_ShouldProxyOnlyMatchingNamespaces()
    {
        var services = new ServiceCollection();
        services.AddTransient<IWhitelistedWorker, WhitelistedWorker>();
        services.AddTransient<INonWhitelistedWorker, NonWhitelistedWorker>();

        services.AddProjectTracing(options =>
        {
            options
                .WithPublicMethodsTracing()
                .WithProxiedNamespacePrefixes("Imlinka.Tests.TestModels.Proxied");
        });

        using var provider = services.BuildServiceProvider();
        var allowed = provider.GetRequiredService<IWhitelistedWorker>();
        var blocked = provider.GetRequiredService<INonWhitelistedWorker>();

        allowed.Should().NotBeOfType<WhitelistedWorker>();
        blocked.Should().BeOfType<NonWhitelistedWorker>();
        allowed.Calculate().Should().Be(11);
        blocked.Calculate().Should().Be(22);
    }

    /// <summary>
    /// Ignore prefixes should still exclude services even if they match proxied namespace prefixes.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenNamespaceMatchesAllowlistButAlsoIgnored_ShouldNotProxyService()
    {
        var services = new ServiceCollection();
        services.AddTransient<IWhitelistedWorker, WhitelistedWorker>();

        services.AddProjectTracing(options =>
        {
            options
                .WithPublicMethodsTracing()
                .WithProxiedNamespacePrefixes("Imlinka.Tests.TestModels.Proxied");

            options.IgnoredNamespacePrefixes.Add("Imlinka.Tests.TestModels.Proxied");
        });

        using var provider = services.BuildServiceProvider();
        var allowed = provider.GetRequiredService<IWhitelistedWorker>();

        allowed.Should().BeOfType<WhitelistedWorker>();
        allowed.Calculate().Should().Be(11);
    }
}