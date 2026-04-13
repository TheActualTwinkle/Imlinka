using FluentAssertions;
using Imlinka.Tests.TestModels.NotProxied;
using Imlinka.Tests.TestModels.Proxied;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Verifies namespace prefix filtering for tracing candidates.
/// </summary>
public sealed class NamespaceFilteringTests
{
    /// <summary>
    /// With namespace allowlist only matching namespace services should be proxied.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenProxiedNamespacePrefixesConfigured_ShouldProxyOnlyMatchingNamespaces()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<IWhitelistedWorker, WhitelistedWorker>();
        services.AddTransient<INonWhitelistedWorker, NonWhitelistedWorker>();

        services.AddProjectTracing(options =>
        {
            options
                .WithPublicMethodsTracing()
                .WithProxiedNamespacePrefixes(["Imlinka.Tests.TestModels.Proxied"]);
        });

        // Act.
        using var provider = services.BuildServiceProvider();
        var allowed = provider.GetRequiredService<IWhitelistedWorker>();
        var blocked = provider.GetRequiredService<INonWhitelistedWorker>();

        // Assert.
        allowed.Should().NotBeOfType<WhitelistedWorker>();
        blocked.Should().BeOfType<NonWhitelistedWorker>();
    }

    /// <summary>
    /// Ignore prefixes should still exclude services even if they match proxied namespace prefixes.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenNamespaceMatchesAllowlistButAlsoIgnored_ShouldNotProxyService()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<IWhitelistedWorker, WhitelistedWorker>();

        services.AddProjectTracing(options =>
        {
            options
                .WithPublicMethodsTracing()
                .WithProxiedNamespacePrefixes(["Imlinka.Tests.TestModels.Proxied"]);

            options.IgnoredNamespacePrefixes.Add("Imlinka.Tests.TestModels.Proxied");
        });

        // Act.
        using var provider = services.BuildServiceProvider();
        var allowed = provider.GetRequiredService<IWhitelistedWorker>();

        // Assert.
        allowed.Should().BeOfType<WhitelistedWorker>();
    }
}