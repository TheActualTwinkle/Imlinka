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
        worker.Should().BeOfType<ImplementationTraceWorker>();
        spans.Should().ContainSingle(a => a.DisplayName == "ImplementationTraceWorker.Important");
        spans.Should().NotContain(a => a.DisplayName == "ImplementationTraceWorker.Plain");
    }

    /// <summary>
    /// [Trace] on interface method should be applied to the implementing method.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenInterfaceMethodMarkedWithTrace_ShouldTraceImplementationMethod()
    {
        var tracedSource = new ActivitySource($"tests.attributes.interface-trace.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IMethodTraceWorker, InterfaceTraceWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMethodTraceWorker>().Work();

        collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .Should()
            .ContainSingle(a => a.DisplayName == "custom.interface.span");
    }

    /// <summary>
    /// [Trace] on closed generic interface method should be applied to the implementing method.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenGenericInterfaceMethodMarkedWithTrace_ShouldTraceImplementationMethod()
    {
        var tracedSource = new ActivitySource($"tests.attributes.generic-interface-trace.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IGenericTraceWorker<string>, GenericInterfaceTraceWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IGenericTraceWorker<string>>();
        worker.Handle("value");
        worker.HandleMany(["one", "two"]);

        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .ToList();

        spans.Should().ContainSingle(a => a.DisplayName == "generic.interface.span");
        spans.Should().ContainSingle(a => a.DisplayName == "generic.interface.collection.span");
    }

    /// <summary>
    /// [Traced] on interface should be applied to public implementation methods.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenInterfaceMarkedWithTraced_ShouldTraceImplementationMethods()
    {
        var tracedSource = new ActivitySource($"tests.attributes.interface-traced.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<ITracedContractWorker, TracedInterfaceWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITracedContractWorker>().Run();

        collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .Should()
            .ContainSingle(a => a.DisplayName == "iface.prefix.TracedInterfaceWorker.Run");
    }

    /// <summary>
    /// [Traced] on implementation class should trace public methods and apply class prefix to span name.
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
        worker.Should().BeOfType<TracedClassWorker>();
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
        worker.Should().BeOfType<TracedAndMethodTraceWorker>();
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
        worker.Should().BeOfType<InheritedTracedWorker>();
        spans.Should().ContainSingle(a => a.DisplayName == "base.prefix.InheritedTracedWorker.Ping");
    }

    /// <summary>
    /// [Trace] on ValueTask methods should keep spans open until returned value tasks complete.
    /// </summary>
    [Fact]
    public async Task AddProjectTracing_WhenValueTaskMethodsAreMarkedWithTrace_ShouldTraceUntilCompletion()
    {
        var tracedSource = new ActivitySource($"tests.attributes.valuetask.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IValueTaskWorker, ValueTaskWorker>();

        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IValueTaskWorker>();

        await worker.RunAsync();
        var result = await worker.CountAsync();

        result.Should().Be(42);
        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .ToList();

        spans.Should().ContainSingle(a => a.DisplayName == "ValueTaskWorker.RunAsync");
        spans.Should().ContainSingle(a => a.DisplayName == "ValueTaskWorker.CountAsync");
        spans.Should().OnlyContain(a => a.Duration > TimeSpan.Zero);
    }

    /// <summary>
    /// Ref-return methods should be left untouched because the generic return-value rewrite is unsafe for them.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenRefReturnMethodMarkedWithTrace_ShouldSkipMethod()
    {
        var tracedSource = new ActivitySource($"tests.attributes.ref-return.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddProjectTracing(options => options.WithActivitySource(tracedSource));

        using var provider = services.BuildServiceProvider();
        var worker = new RefReturnWorker();

        ref var value = ref worker.GetValue();
        value.Should().Be(42);

        collector.Started
            .Where(a => a.Source.Name == tracedSource.Name)
            .Should()
            .BeEmpty();
    }
}
