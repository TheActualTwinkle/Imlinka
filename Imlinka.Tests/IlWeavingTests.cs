using System.Diagnostics;
using FluentAssertions;
using Imlinka.Tests.Diagnostics;
using Imlinka.Tests.TestModels;
using Imlinka.Tests.TestModels.NotProxied;
using Imlinka.Tests.TestModels.Proxied;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Verifies spans created by build-time IL weaving.
/// </summary>
public sealed class IlWeavingTests
{
    [Fact]
    public void AddProjectTracing_WhenPublicMethodsTracingEnabled_ShouldTracePublicMethodsAndKeepConcreteType()
    {
        var source = new ActivitySource($"tests.il.public.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();
        services.AddProjectTracing(options => options
            .WithPublicMethodsTracing()
            .WithActivitySource(source));

        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<ICompatibleWorker>();

        worker.Do();

        worker.Should().BeOfType<CompatibleWorker>();
        collector.Started
            .Where(a => a.Source.Name == source.Name)
            .Should()
            .ContainSingle(a => a.DisplayName == "CompatibleWorker.Do");
    }

    [Fact]
    public void AddProjectTracing_WhenPublicMethodsTracingDisabledAndNoAttributes_ShouldNotTracePlainPublicMethods()
    {
        var source = new ActivitySource($"tests.il.plain.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IPlainWorker, PlainWorker>();
        services.AddProjectTracing(options => options.WithActivitySource(source));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPlainWorker>().Work();

        collector.Started
            .Where(a => a.Source.Name == source.Name)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void AddProjectTracing_WhenNamespaceAllowlistConfigured_ShouldTraceOnlyMatchingNamespaces()
    {
        var source = new ActivitySource($"tests.il.namespaces.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IWhitelistedWorker, WhitelistedWorker>();
        services.AddTransient<INonWhitelistedWorker, NonWhitelistedWorker>();
        services.AddProjectTracing(options => options
            .WithPublicMethodsTracing()
            .WithActivitySource(source)
            .WithTracedNamespacePrefixesOnly(["Imlinka.Tests.TestModels.Proxied"]));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IWhitelistedWorker>().Run();
        provider.GetRequiredService<INonWhitelistedWorker>().Run();

        var spans = collector.Started
            .Where(a => a.Source.Name == source.Name)
            .ToList();

        spans.Should().ContainSingle(a => a.DisplayName == "WhitelistedWorker.Run");
        spans.Should().NotContain(a => a.DisplayName == "NonWhitelistedWorker.Run");
    }

    [Fact]
    public void AddProjectTracing_WhenNamespaceIgnored_ShouldSuppressMatchingNamespace()
    {
        var source = new ActivitySource($"tests.il.ignore.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IWhitelistedWorker, WhitelistedWorker>();
        services.AddProjectTracing(options =>
        {
            options
                .WithPublicMethodsTracing()
                .WithActivitySource(source)
                .WithTracedNamespacePrefixesOnly(["Imlinka.Tests.TestModels.Proxied"]);

            options.IgnoredNamespacePrefixes.Add("Imlinka.Tests.TestModels.Proxied");
        });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IWhitelistedWorker>().Run();

        collector.Started
            .Where(a => a.Source.Name == source.Name)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void AddProjectTracingForAssembly_WhenAssemblyMatches_ShouldTraceMatchingAssembly()
    {
        var source = new ActivitySource($"tests.il.assembly.match.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();
        services.AddProjectTracingForAssembly(typeof(CompatibleWorker).Assembly, options => options
            .WithPublicMethodsTracing()
            .WithActivitySource(source));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICompatibleWorker>().Do();

        collector.Started
            .Where(a => a.Source.Name == source.Name)
            .Should()
            .ContainSingle(a => a.DisplayName == "CompatibleWorker.Do");
    }

    [Fact]
    public void AddProjectTracingForAssembly_WhenAssemblyDoesNotMatch_ShouldSuppressOtherAssemblies()
    {
        var source = new ActivitySource($"tests.il.assembly.mismatch.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();
        services.AddProjectTracingForAssembly(typeof(string).Assembly, options => options
            .WithPublicMethodsTracing()
            .WithActivitySource(source));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICompatibleWorker>().Do();

        collector.Started
            .Where(a => a.Source.Name == source.Name)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void AddProjectTracingForAssembly_WhenCalledMultipleTimes_ShouldMergeAssemblyFilters()
    {
        var source = new ActivitySource($"tests.il.assembly.merge.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();
        services.AddProjectTracingForAssembly(typeof(string).Assembly, options => options
            .WithPublicMethodsTracing()
            .WithActivitySource(source));
        services.AddProjectTracingForAssembly(typeof(CompatibleWorker).Assembly, options => options
            .WithPublicMethodsTracing()
            .WithActivitySource(source));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICompatibleWorker>().Do();

        collector.Started
            .Where(a => a.Source.Name == source.Name)
            .Should()
            .ContainSingle(a => a.DisplayName == "CompatibleWorker.Do");
    }

    [Fact]
    public void AddProjectTracingForAssemblies_WhenAssemblyCollectionIsEmpty_ShouldSuppressAllAssemblies()
    {
        var source = new ActivitySource($"tests.il.assembly.empty.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();
        services.AddProjectTracingForAssemblies([], options => options
            .WithPublicMethodsTracing()
            .WithActivitySource(source));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICompatibleWorker>().Do();

        collector.Started
            .Where(a => a.Source.Name == source.Name)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void AddProjectTracingForAssembly_WhenGlobalTracingWasAlreadyConfigured_ShouldKeepGlobalAssemblyScope()
    {
        var source = new ActivitySource($"tests.il.assembly.global-then-filter.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();
        services.AddProjectTracing(options => options
            .WithPublicMethodsTracing()
            .WithActivitySource(source));
        services.AddProjectTracingForAssembly(typeof(string).Assembly, options => options
            .WithActivitySource(source));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICompatibleWorker>().Do();

        collector.Started
            .Where(a => a.Source.Name == source.Name)
            .Should()
            .ContainSingle(a => a.DisplayName == "CompatibleWorker.Do");
    }

    [Fact]
    public void AddProjectTracing_WhenServiceHasTransientLifetime_ShouldKeepConcreteTypeAndLifetime()
    {
        var source = new ActivitySource($"tests.il.lifetime.{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddTransient<ILifetimeProbe, LifetimeProbe>();
        services.AddProjectTracing(options => options
            .WithPublicMethodsTracing()
            .WithActivitySource(source));

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ILifetimeProbe>();
        var second = provider.GetRequiredService<ILifetimeProbe>();

        first.Should().BeOfType<LifetimeProbe>();
        second.Should().BeOfType<LifetimeProbe>();
        first.InstanceId().Should().NotBe(second.InstanceId());
    }
}
