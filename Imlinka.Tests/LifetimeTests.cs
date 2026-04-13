using FluentAssertions;
using Imlinka.Tests.TestModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Verifies that tracing registration keeps original DI lifetimes.
/// </summary>
public sealed class LifetimeTests
{
    /// <summary>
    /// Transient service should produce different implementation instances on each resolve.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenTransientServiceIsRegistered_ShouldPreserveTransientLifetime()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddTransient<ILifetimeProbe, LifetimeProbe>();

        services.AddProjectTracing(options => options.WithPublicMethodsTracing());

        // Act.
        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ILifetimeProbe>();
        var second = provider.GetRequiredService<ILifetimeProbe>();

        // Assert.
        first.Should().NotBeOfType<LifetimeProbe>();
        second.Should().NotBeOfType<LifetimeProbe>();
        first.InstanceId().Should().NotBe(second.InstanceId());
    }

    /// <summary>
    /// Scoped service should be stable inside a scope and different across scopes.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenScopedServiceIsRegistered_ShouldPreserveScopedLifetime()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddScoped<ILifetimeProbe, LifetimeProbe>();

        services.AddProjectTracing(options => options.WithPublicMethodsTracing());

        // Act.
        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        var firstInScope = scope1.ServiceProvider.GetRequiredService<ILifetimeProbe>();
        var secondInScope = scope1.ServiceProvider.GetRequiredService<ILifetimeProbe>();

        using var scope2 = provider.CreateScope();
        var inAnotherScope = scope2.ServiceProvider.GetRequiredService<ILifetimeProbe>();

        // Assert.
        firstInScope.Should().NotBeOfType<LifetimeProbe>();
        firstInScope.InstanceId().Should().Be(secondInScope.InstanceId());
        firstInScope.InstanceId().Should().NotBe(inAnotherScope.InstanceId());
    }

    /// <summary>
    /// Singleton service should keep one implementation instance across scopes.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenSingletonServiceIsRegistered_ShouldPreserveSingletonLifetime()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<ILifetimeProbe, LifetimeProbe>();

        services.AddProjectTracing(options => options.WithPublicMethodsTracing());

        // Act.
        using var provider = services.BuildServiceProvider();
        var fromRoot = provider.GetRequiredService<ILifetimeProbe>();

        using var scope = provider.CreateScope();
        var fromScope = scope.ServiceProvider.GetRequiredService<ILifetimeProbe>();

        // Assert.
        fromRoot.Should().NotBeOfType<LifetimeProbe>();
        fromRoot.InstanceId().Should().Be(fromScope.InstanceId());
    }
}