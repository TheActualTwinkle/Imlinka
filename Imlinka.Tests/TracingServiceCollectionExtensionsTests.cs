using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Tests for tracing registrations that rely on DispatchProxy and interface shape filtering.
/// </summary>
public sealed class TracingServiceCollectionExtensionsTests
{
    /// <summary>
    /// AddProjectTracingForAssembly when interface is DispatchProxy-compatible should return a proxied service instance.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_CompatibleInterface_ReturnsProxyInstance()
    {
        var services = new ServiceCollection();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();

        services.AddProjectTracingForAssembly(typeof(CompatibleWorker).Assembly, options =>
        {
            options.TraceAllPublicMethods = true;
        });

        var serviceProvider = services.BuildServiceProvider();
        var worker = serviceProvider.GetRequiredService<ICompatibleWorker>();

        worker.Should().NotBeNull();
        worker.Should().NotBeOfType<CompatibleWorker>();
        worker.Calculate().Should().Be(42);
    }

    /// <summary>
    /// AddProjectTracingForAssembly when interface has ReadOnlySpan parameter should keep original implementation without proxy.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_ReadOnlySpanParameter_ReturnsOriginalImplementation()
    {
        var services = new ServiceCollection();
        services.AddTransient<ISpanDeserializer, SpanDeserializer>();

        services.AddProjectTracingForAssembly(typeof(SpanDeserializer).Assembly, options =>
        {
            options.TraceAllPublicMethods = true;
        });

        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<ISpanDeserializer>();
        var value = Guid.NewGuid();

        serializer.Should().BeOfType<SpanDeserializer>();
        serializer.Deserialize(value.ToByteArray(), false).Should().Be(value);
    }

    /// <summary>
    /// AddProjectTracingForAssembly when interface has byref-like return should keep original implementation without proxy.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_ByRefLikeReturnType_ReturnsOriginalImplementation()
    {
        var services = new ServiceCollection();
        services.AddTransient<ISpanSource, SpanSource>();

        services.AddProjectTracingForAssembly(typeof(SpanSource).Assembly, options =>
        {
            options.TraceAllPublicMethods = true;
        });

        var serviceProvider = services.BuildServiceProvider();
        var source = serviceProvider.GetRequiredService<ISpanSource>();

        source.Should().BeOfType<SpanSource>();
        source.GetFirstByte().Should().Be(1);
    }

    /// <summary>
    /// AddProjectTracingForAssembly when interface mixes safe and byref-like methods should skip proxy for entire interface.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_MixedInterfaceWithSpanMethod_ReturnsOriginalImplementation()
    {
        var services = new ServiceCollection();
        services.AddTransient<IMixedSpanInterface, MixedSpanImplementation>();

        services.AddProjectTracingForAssembly(typeof(MixedSpanImplementation).Assembly, options =>
        {
            options.TraceAllPublicMethods = true;
        });

        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<IMixedSpanInterface>();

        service.Should().BeOfType<MixedSpanImplementation>();
        service.Ping().Should().Be("ok");
    }

    /// <summary>
    /// AddProjectTracing when TraceAllPublicMethods is false and no attributes are present should keep original implementation.
    /// </summary>
    [Fact]
    public void AddProjectTracing_NoTracingAttributesAndTraceAllDisabled_ReturnsOriginalImplementation()
    {
        var services = new ServiceCollection();
        services.AddTransient<IPlainWorker, PlainWorker>();

        services.AddProjectTracing(_ =>
        {
        });

        var serviceProvider = services.BuildServiceProvider();
        var worker = serviceProvider.GetRequiredService<IPlainWorker>();

        worker.Should().BeOfType<PlainWorker>();
        worker.Calculate().Should().Be(7);
    }

    /// <summary>
    /// AddProjectTracing when TraceAllPublicMethods is enabled and interface has in ReadOnlySpan parameter should keep original implementation.
    /// </summary>
    [Fact]
    public void AddProjectTracing_InReadOnlySpanParameterWithTraceAllEnabled_ReturnsOriginalImplementation()
    {
        var services = new ServiceCollection();
        services.AddTransient<IInSpanConsumer, InSpanConsumer>();

        services.AddProjectTracing(options =>
        {
            options.TraceAllPublicMethods = true;
        });

        var serviceProvider = services.BuildServiceProvider();
        var consumer = serviceProvider.GetRequiredService<IInSpanConsumer>();
        var bytes = new byte[] { 1, 2, 3 };

        consumer.Should().BeOfType<InSpanConsumer>();
        consumer.Count(bytes).Should().Be(3);
    }

    /// <summary>
    /// AddProjectTracingForAssembly when unsupported and supported interfaces are both registered should proxy only supported service.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_MixedSupportedAndUnsupportedRegistrations_ProxiesOnlySupportedService()
    {
        var services = new ServiceCollection();
        services.AddTransient<ISpanDeserializer, SpanDeserializer>();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();

        services.AddProjectTracingForAssembly(typeof(CompatibleWorker).Assembly, options =>
        {
            options.TraceAllPublicMethods = true;
        });

        var serviceProvider = services.BuildServiceProvider();
        var spanDeserializer = serviceProvider.GetRequiredService<ISpanDeserializer>();
        var worker = serviceProvider.GetRequiredService<ICompatibleWorker>();

        spanDeserializer.Should().BeOfType<SpanDeserializer>();
        worker.Should().NotBeOfType<CompatibleWorker>();
    }

    /// <summary>
    /// AddProjectTracingForAssembly when interface uses ReadOnlyMemory parameter should still proxy because type is not byref-like.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_ReadOnlyMemoryParameter_ReturnsProxyInstance()
    {
        var services = new ServiceCollection();
        services.AddTransient<IMemoryConsumer, MemoryConsumer>();

        services.AddProjectTracingForAssembly(typeof(MemoryConsumer).Assembly, options =>
        {
            options.TraceAllPublicMethods = true;
        });

        var serviceProvider = services.BuildServiceProvider();
        var consumer = serviceProvider.GetRequiredService<IMemoryConsumer>();

        consumer.Should().NotBeNull();
        consumer.Should().NotBeOfType<MemoryConsumer>();
        consumer.Count(new ReadOnlyMemory<byte>([1, 2, 3, 4])).Should().Be(4);
    }

    /// <summary>
    /// AddProjectTracing when TraceAllPublicMethods is enabled for mixed interface with ReadOnlySpan should skip proxy for entire service.
    /// </summary>
    [Fact]
    public void AddProjectTracing_MixedInterfaceWithSpanAndTraceAllEnabled_ReturnsOriginalImplementation()
    {
        var services = new ServiceCollection();
        services.AddTransient<IMixedSpanInterface, MixedSpanImplementation>();

        services.AddProjectTracing(options =>
        {
            options.TraceAllPublicMethods = true;
        });

        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<IMixedSpanInterface>();

        service.Should().BeOfType<MixedSpanImplementation>();
        service.Ping().Should().Be("ok");
    }

    /// <summary>
    /// AddProjectTracing when mixed interface is marked with Traced should still skip proxy due ReadOnlySpan method incompatibility.
    /// </summary>
    [Fact]
    public void AddProjectTracing_TracedAttributeOnMixedInterface_ReturnsOriginalImplementation()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITracedMixedSpanInterface, TracedMixedSpanImplementation>();

        services.AddProjectTracing(_ =>
        {
        });

        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ITracedMixedSpanInterface>();

        service.Should().BeOfType<TracedMixedSpanImplementation>();
        service.Ping().Should().Be("traced-ok");
    }

    /// <summary>
    /// AddProjectTracing when proxy-compatible interface is marked with Traced should return proxy instance.
    /// </summary>
    [Fact]
    public void AddProjectTracing_TracedAttributeOnCompatibleInterface_ReturnsProxyInstance()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITracedCompatibleWorker, TracedCompatibleWorker>();

        services.AddProjectTracing(_ =>
        {
        });

        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ITracedCompatibleWorker>();

        service.Should().NotBeNull();
        service.Should().NotBeOfType<TracedCompatibleWorker>();
        service.Calculate().Should().Be(100);
    }
}


