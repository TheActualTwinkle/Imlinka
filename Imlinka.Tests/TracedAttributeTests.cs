using System.Diagnostics;
using FluentAssertions;
using Imlinka.Tests.Diagnostics;
using Imlinka.Tests.TestModels.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Verifies tracing registration and span naming behavior for [Trace]/[Traced] attributes.
/// </summary>
public sealed class TracedAttributeTests
{
    /// <summary>
    /// [Trace] on interface method should proxy service and use explicit span name.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenInterfaceMethodMarkedWithTrace_ShouldUseExplicitTraceSpanName()
    {
        // Arrange.
        var tracedSource = new ActivitySource($"tests.attributes.interface-trace.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IMethodTraceWorker, InterfaceTraceWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IMethodTraceWorker>();

        // Act.
        worker.Work();
        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .ToList();

        // Assert.
        worker.Should().NotBeOfType<InterfaceTraceWorker>();
        spans.Should().ContainSingle(a => a.DisplayName == "custom.interface.span");
    }

    /// <summary>
    /// [Trace] on implementation method should trace only the marked method when TraceAllPublicMethods is disabled.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenImplementationMethodMarkedWithTrace_ShouldTraceOnlyMarkedMethod()
    {
        // Arrange.
        var tracedSource = new ActivitySource($"tests.attributes.impl-trace.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IImplementationTraceWorker, ImplementationTraceWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IImplementationTraceWorker>();

        // Act.
        worker.Important();
        worker.Plain();
        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .ToList();

        // Assert.
        worker.Should().NotBeOfType<ImplementationTraceWorker>();
        spans.Should().ContainSingle(a => a.DisplayName == "ImplementationTraceWorker.Important");
        spans.Should().NotContain(a => a.DisplayName == "ImplementationTraceWorker.Plain");
    }

    /// <summary>
    /// [Traced] on interface should proxy service and apply interface prefix to span name.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenInterfaceMarkedWithTraced_ShouldUseInterfacePrefixInSpanName()
    {
        // Arrange.
        var tracedSource = new ActivitySource($"tests.attributes.interface-traced.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ITracedContractWorker, TracedInterfaceWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<ITracedContractWorker>();

        // Act.
        worker.Run();
        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .ToList();

        // Assert.
        worker.Should().NotBeOfType<TracedInterfaceWorker>();
        spans.Should().ContainSingle(a => a.DisplayName == "iface.prefix.TracedInterfaceWorker.Run");
    }

    /// <summary>
    /// [Traced] on implementation class should proxy service and apply class prefix to span name.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenImplementationMarkedWithTraced_ShouldUseClassPrefixInSpanName()
    {
        // Arrange.
        var tracedSource = new ActivitySource($"tests.attributes.class-traced.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ITracedClassWorker, TracedClassWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<ITracedClassWorker>();

        // Act.
        worker.Execute();
        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .ToList();

        // Assert.
        worker.Should().NotBeOfType<TracedClassWorker>();
        spans.Should().ContainSingle(a => a.DisplayName == "class.prefix.TracedClassWorker.Execute");
    }

    /// <summary>
    /// [Trace] with explicit span name should override [Traced] prefix on the same implementation.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenTraceAndTracedUsedTogether_ShouldPreferTraceSpanName()
    {
        // Arrange.
        var tracedSource = new ActivitySource($"tests.attributes.trace-overrides-traced.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ITracedAndMethodTraceWorker, TracedAndMethodTraceWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<ITracedAndMethodTraceWorker>();

        // Act.
        worker.Compute();
        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .ToList();

        // Assert.
        worker.Should().NotBeOfType<TracedAndMethodTraceWorker>();
        spans.Should().ContainSingle(a => a.DisplayName == "override.span");
    }

    /// <summary>
    /// [Traced] inherited from base implementation type should still trigger tracing.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenImplementationInheritsTraced_ShouldTraceUsingInheritedPrefix()
    {
        // Arrange.
        var tracedSource = new ActivitySource($"tests.attributes.inherited-traced.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IInheritedTracedWorker, InheritedTracedWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IInheritedTracedWorker>();

        // Act.
        worker.Ping();
        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .ToList();

        // Assert.
        worker.Should().NotBeOfType<InheritedTracedWorker>();
        spans.Should().ContainSingle(a => a.DisplayName == "base.prefix.InheritedTracedWorker.Ping");
    }
}