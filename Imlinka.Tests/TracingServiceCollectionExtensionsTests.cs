using FluentAssertions;
using Imlinka.Tests.TestModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Tests for tracing registrations that rely on DispatchProxy and interface shape filtering.
/// </summary>
public sealed class TracingServiceCollectionRegistrationTests
{
    /// <summary>
    /// AddProjectTracingForAssembly when interface is DispatchProxy-compatible should return a proxied service instance.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_WhenInterfaceIsCompatible_ShouldReturnProxyInstance()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();

        services.AddProjectTracingForAssembly(typeof(CompatibleWorker).Assembly, options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var worker = serviceProvider.GetRequiredService<ICompatibleWorker>();

        // Assert.
        worker.Should().NotBeNull();
        worker.Should().NotBeOfType<CompatibleWorker>();
    }

    /// <summary>
    /// AddProjectTracingForAssembly when interface has ReadOnlySpan parameter should keep original implementation without proxy.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_WhenInterfaceHasReadOnlySpanParameter_ShouldReturnOriginalImplementation()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<ISpanDeserializer, SpanDeserializer>();

        services.AddProjectTracingForAssembly(typeof(SpanDeserializer).Assembly, options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<ISpanDeserializer>();
        var value = Guid.NewGuid();

        // Assert.
        serializer.Should().BeOfType<SpanDeserializer>();
        serializer.Deserialize(value.ToByteArray(), false).Should().Be(value);
    }

    /// <summary>
    /// AddProjectTracingForAssembly when interface has byref-like return should keep original implementation without proxy.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_WhenInterfaceHasByRefLikeReturnType_ShouldReturnOriginalImplementation()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<ISpanSource, SpanSource>();

        services.AddProjectTracingForAssembly(typeof(SpanSource).Assembly, options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var source = serviceProvider.GetRequiredService<ISpanSource>();

        // Assert.
        source.Should().BeOfType<SpanSource>();
    }

    /// <summary>
    /// AddProjectTracingForAssembly when interface mixes safe and byref-like methods should skip proxy for entire interface.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_WhenInterfaceContainsSpanMethod_ShouldReturnOriginalImplementation()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<IMixedSpanWorker, MixedSpanImplementation>();

        services.AddProjectTracingForAssembly(typeof(MixedSpanImplementation).Assembly, options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<IMixedSpanWorker>();

        // Assert.
        service.Should().BeOfType<MixedSpanImplementation>();
    }

    /// <summary>
    /// AddProjectTracing when TraceAllPublicMethods is false and no attributes are present should keep original implementation.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenNoTracingAttributesAndTraceAllDisabled_ShouldReturnOriginalImplementation()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<IPlainWorker, PlainWorker>();

        services.AddProjectTracing(_ =>
        {
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var worker = serviceProvider.GetRequiredService<IPlainWorker>();

        // Assert.
        worker.Should().BeOfType<PlainWorker>();
    }

    /// <summary>
    /// AddProjectTracing when TraceAllPublicMethods is enabled and interface has in ReadOnlySpan parameter should keep original implementation.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenInterfaceHasInReadOnlySpanParameterAndTraceAllEnabled_ShouldReturnOriginalImplementation()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<IInSpanConsumer, InSpanConsumer>();

        services.AddProjectTracing(options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var consumer = serviceProvider.GetRequiredService<IInSpanConsumer>();

        // Assert.
        consumer.Should().BeOfType<InSpanConsumer>();
    }

    /// <summary>
    /// AddProjectTracingForAssembly when unsupported and supported interfaces are both registered should proxy only supported service.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_WhenSupportedAndUnsupportedServicesAreRegistered_ShouldProxyOnlySupportedService()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<ISpanDeserializer, SpanDeserializer>();
        services.AddTransient<ICompatibleWorker, CompatibleWorker>();

        services.AddProjectTracingForAssembly(typeof(CompatibleWorker).Assembly, options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var spanDeserializer = serviceProvider.GetRequiredService<ISpanDeserializer>();
        var worker = serviceProvider.GetRequiredService<ICompatibleWorker>();

        // Assert.
        spanDeserializer.Should().BeOfType<SpanDeserializer>();
        worker.Should().NotBeOfType<CompatibleWorker>();
    }

    /// <summary>
    /// AddProjectTracingForAssembly when interface uses ReadOnlyMemory parameter should still proxy because type is not byref-like.
    /// </summary>
    [Fact]
    public void AddProjectTracingForAssembly_WhenInterfaceHasReadOnlyMemoryParameter_ShouldReturnProxyInstance()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<IMemoryConsumer, MemoryConsumer>();

        services.AddProjectTracingForAssembly(typeof(MemoryConsumer).Assembly, options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var consumer = serviceProvider.GetRequiredService<IMemoryConsumer>();

        // Assert.
        consumer.Should().NotBeNull();
        consumer.Should().NotBeOfType<MemoryConsumer>();
    }

    /// <summary>
    /// AddProjectTracing when TraceAllPublicMethods is enabled for mixed interface with ReadOnlySpan should skip proxy for entire service.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenMixedInterfaceContainsSpanAndTraceAllEnabled_ShouldReturnOriginalImplementation()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<IMixedSpanWorker, MixedSpanImplementation>();

        services.AddProjectTracing(options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<IMixedSpanWorker>();

        // Assert.
        service.Should().BeOfType<MixedSpanImplementation>();
    }

    /// <summary>
    /// AddProjectTracing when mixed interface is marked with Traced should still skip proxy due ReadOnlySpan method incompatibility.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenMixedInterfaceIsTracedButContainsSpanMethod_ShouldReturnOriginalImplementation()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<ITracedMixedSpanWorker, TracedMixedSpanImplementation>();

        services.AddProjectTracing(_ =>
        {
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ITracedMixedSpanWorker>();

        // Assert.
        service.Should().BeOfType<TracedMixedSpanImplementation>();
    }

    /// <summary>
    /// AddProjectTracing when proxy-compatible interface is marked with Traced should return proxy instance.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenCompatibleInterfaceIsTraced_ShouldReturnProxyInstance()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<ITracedCompatibleWorker, TracedCompatibleWorker>();

        services.AddProjectTracing(_ =>
        {
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ITracedCompatibleWorker>();

        // Assert.
        service.Should().NotBeNull();
        service.Should().NotBeOfType<TracedCompatibleWorker>();
    }

    /// <summary>
    /// AddProjectTracing when registration uses implementation factory should still return proxied interface instance and keep factory functionality.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenRegisteredViaImplementationFactory_ShouldReturnProxyInstanceAndKeepFactoryFunctionality()
    {
        // Arrange.
        const string text = "test";
        
        var services = new ServiceCollection();
        services.AddScoped<IFactoryJumper>(_ => new FactoryJumper(text));

        services.AddProjectTracing(options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var jumper = serviceProvider.GetRequiredService<IFactoryJumper>();

        // Assert.
        jumper.Should().NotBeNull();
        jumper.Should().NotBeOfType<FactoryJumper>();
        jumper.Text.Should().Be(text);
    }

    /// <summary>
    /// AddProjectTracing when registration uses keyed implementation type should return keyed proxy instance.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenKeyedImplementationTypeIsRegistered_ShouldReturnProxyInstance()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddKeyedScoped<IKeyedWorker, KeyedWorker>("main");

        services.AddProjectTracing(options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var worker = serviceProvider.GetRequiredKeyedService<IKeyedWorker>("main");

        // Assert.
        worker.Should().NotBeNull();
        worker.Should().NotBeOfType<KeyedWorker>();
    }

    /// <summary>
    /// AddProjectTracing when registration uses AnyKey should return proxy instance for arbitrary key resolve.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenKeyedImplementationTypeIsRegisteredWithAnyKey_ShouldReturnProxyInstanceForRequestedKey()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddKeyedScoped<IKeyedWorker, KeyedWorker>(KeyedService.AnyKey);

        services.AddProjectTracing(options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var worker = serviceProvider.GetRequiredKeyedService<IKeyedWorker>("tenant-42");

        // Assert.
        worker.Should().NotBeNull();
        worker.Should().NotBeOfType<KeyedWorker>();
    }

    /// <summary>
    /// AddProjectTracing when registration uses keyed implementation factory should return keyed proxy instance.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenKeyedImplementationFactoryIsRegistered_ShouldReturnProxyInstance()
    {
        // Arrange.
        const string text = "test";
        
        var services = new ServiceCollection();
        services.AddKeyedScoped<IKeyedFactoryWorker>("dynamic", (_, _) => new KeyedFactoryWorker(text));

        services.AddProjectTracing(options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var worker = serviceProvider.GetRequiredKeyedService<IKeyedFactoryWorker>("dynamic");

        // Assert.
        worker.Should().NotBeNull();
        worker.Should().NotBeOfType<KeyedFactoryWorker>();
        worker.Text.Should().Be(text);
    }

    /// <summary>
    /// AddProjectTracing without KeepOriginalService should not leave concrete implementation resolvable by type.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenKeepOriginalServiceIsNotEnabled_ShouldNotResolveConcreteImplementation()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddScoped<ICompatibleWorker, CompatibleWorker>();

        services.AddProjectTracing(options =>
        {
            options.WithPublicMethodsTracing();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();

        // Assert.
        serviceProvider.GetService<CompatibleWorker>().Should().BeNull();
    }

    /// <summary>
    /// AddProjectTracing with KeepOriginalService should keep concrete implementation resolvable together with proxied interface.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenKeepOriginalServiceIsEnabled_ShouldResolveConcreteImplementationAndProxy()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddScoped<ICompatibleWorker, CompatibleWorker>();

        services.AddProjectTracing(options =>
        {
            options
                .WithPublicMethodsTracing()
                .KeepOriginalService();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ICompatibleWorker>();
        var concrete = serviceProvider.GetRequiredService<CompatibleWorker>();

        // Assert.
        service.Should().NotBeOfType<CompatibleWorker>();
        concrete.Should().BeOfType<CompatibleWorker>();
    }

    /// <summary>
    /// AddProjectTracing with KeepOriginalService should keep concrete implementation resolvable together with proxied interface even for registrations with implementation factory.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenKeepOriginalServiceIsEnabledAndRegisteredWithImplementationFactory_ShouldResolveConcreteImplementationAndProxy()
    {
        // Arrange.
        const string text = "test";
        
        var services = new ServiceCollection();
        services.AddScoped<ICompatibleWorker>(_ => new CompatibleWorker(text));

        services.AddProjectTracing(options =>
        {
            options
                .WithPublicMethodsTracing()
                .KeepOriginalService();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ICompatibleWorker>();
        var concrete = serviceProvider.GetRequiredService<CompatibleWorker>();

        // Assert.
        service.Should().NotBeOfType<CompatibleWorker>();
        concrete.Should().BeOfType<CompatibleWorker>();
        
        service.Text.Should().Be(text);
        concrete.Text.Should().Be(text);
    }
    
    /// <summary>
    /// AddProjectTracing with KeepOriginalService should keep concrete implementation resolvable together with proxied interface even for keyed registrations.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenKeepOriginalServiceIsEnabledAndRegisteredKeyedService_ShouldResolveConcreteImplementationAndProxy()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddKeyedScoped<IPlainWorker, PlainWorker>("key");

        services.AddProjectTracing(options =>
        {
            options
                .WithPublicMethodsTracing()
                .KeepOriginalService();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredKeyedService<IPlainWorker>("key");
        var concrete = serviceProvider.GetRequiredKeyedService<PlainWorker>("key");

        // Assert.
        service.Should().NotBeOfType<PlainWorker>();
        concrete.Should().BeOfType<PlainWorker>();
    }
    
    /// <summary>
    /// AddProjectTracing with KeepOriginalService should keep concrete implementation resolvable together with proxied interface even for keyed registrations with factory.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenKeepOriginalServiceIsEnabledAndRegisteredKeyedServiceWithFactoryImplementation_ShouldResolveConcreteImplementationAndProxy()
    {
        // Arrange.
        const string text = "test";
        
        var services = new ServiceCollection();
        services.AddKeyedScoped<ICompatibleWorker>("key", (_, _) => new CompatibleWorker(text));

        services.AddProjectTracing(options =>
        {
            options
                .WithPublicMethodsTracing()
                .KeepOriginalService();
        });

        // Act.
        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredKeyedService<ICompatibleWorker>("key");
        var concrete = serviceProvider.GetRequiredKeyedService<CompatibleWorker>("key");

        // Assert.
        service.Should().NotBeOfType<CompatibleWorker>();
        concrete.Should().BeOfType<CompatibleWorker>();
        
        service.Text.Should().Be(text);
        concrete.Text.Should().Be(text);
    }
}