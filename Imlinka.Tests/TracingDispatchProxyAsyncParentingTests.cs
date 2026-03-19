using System.Diagnostics;
using FluentAssertions;
using Imlinka.Tests.Diagnostics;
using Imlinka.Tests.TestModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Verifies parent-child relationships for async spans created by tracing proxy.
/// </summary>
public sealed class TracingDispatchProxyAsyncParentingTests
{
    /// <summary>
    /// Four sequential async calls should stay sibling spans under the outer request activity.
    /// </summary>
    [Fact]
    public async Task AddProjectTracing_WhenSequentialAsyncCallsExecuted_ShouldKeepSameParentAcrossFourCalls()
    {
        var tracedSource = new ActivitySource($"tests.imlinka.async.{Guid.NewGuid():N}");
        var requestSource = new ActivitySource($"tests.request.async.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IEventArchive, EventArchive>();

        services.AddProjectTracing(options =>
        {
            options.TraceAllPublicMethods = true;
            options.WithActivitySource(tracedSource);
        });

        using var provider = services.BuildServiceProvider();
        var archive = provider.GetRequiredService<IEventArchive>();

        using var request = requestSource.StartActivity();
        request.Should().NotBeNull();
        var requestSpanId = request.SpanId;

        await archive.PullChunkAsync(10, 5);
        await archive.PullChunkAsync(20, 5);
        await archive.PullChunkAsync(30, 5);
        await archive.PullChunkAsync(40, 5);

        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name && a.DisplayName == "EventArchive.PullChunkAsync")
            .ToList();

        spans.Should().HaveCount(4);
        spans.Should().OnlyContain(s => s.ParentSpanId == requestSpanId);
        var spanIds = spans.Select(s => s.SpanId).ToHashSet();
        spans.Should().OnlyContain(s => !spanIds.Contains(s.ParentSpanId));
    }

    /// <summary>
    /// Four parallel async calls should not be nested under each other.
    /// </summary>
    [Fact]
    public async Task AddProjectTracing_WhenParallelAsyncCallsExecuted_ShouldNotNestAcrossFourCalls()
    {
        var tracedSource = new ActivitySource($"tests.imlinka.async.{Guid.NewGuid():N}");
        var requestSource = new ActivitySource($"tests.request.async.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IEventArchive, EventArchive>();

        services.AddProjectTracing(options =>
        {
            options.TraceAllPublicMethods = true;
            options.WithActivitySource(tracedSource);
        });

        using var provider = services.BuildServiceProvider();
        var archive = provider.GetRequiredService<IEventArchive>();

        using var request = requestSource.StartActivity();
        request.Should().NotBeNull();
        var requestSpanId = request.SpanId;

        var first = archive.PullChunkAsync(50, 5);
        var second = archive.PullChunkAsync(60, 5);
        var third = archive.PullChunkAsync(70, 5);
        var fourth = archive.PullChunkAsync(80, 5);

        await Task.WhenAll(first, second, third, fourth);

        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name && a.DisplayName == "EventArchive.PullChunkAsync")
            .ToList();

        spans.Should().HaveCount(4);
        spans.Should().OnlyContain(s => s.ParentSpanId == requestSpanId);
        var spanIds = spans.Select(s => s.SpanId).ToHashSet();
        spans.Should().OnlyContain(s => !spanIds.Contains(s.ParentSpanId));
    }
}