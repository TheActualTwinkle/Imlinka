using System.Diagnostics;
using FluentAssertions;
using Imlinka.Tests.Diagnostics;
using Imlinka.Tests.TestModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Verifies parent-child relationships for sync spans created by tracing proxy.
/// </summary>
public sealed class TracingDispatchProxySyncParentingTests
{
    /// <summary>
    /// Four sequential sync calls should stay sibling spans under the outer request activity.
    /// </summary>
    [Fact]
    public void AddProjectTracing_WhenSequentialSyncCallsExecuted_ShouldKeepSameParentAcrossFourCalls()
    {
        var tracedSource = new ActivitySource($"tests.imlinka.sync.{Guid.NewGuid():N}");
        var requestSource = new ActivitySource($"tests.request.sync.{Guid.NewGuid():N}");
        using var collector = new ActivityCollector();

        var services = new ServiceCollection();
        services.AddTransient<IRecordCatalog, RecordCatalog>();

        services.AddProjectTracing(options =>
        {
            options.TraceAllPublicMethods = true;
            options.WithActivitySource(tracedSource);
        });

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IRecordCatalog>();

        using var request = requestSource.StartActivity();
        request.Should().NotBeNull();
        var requestSpanId = request.SpanId;

        catalog.ReadPage(1, 10).Should().Be(11);
        catalog.ReadPage(2, 10).Should().Be(12);
        catalog.ReadPage(3, 10).Should().Be(13);
        catalog.ReadPage(4, 10).Should().Be(14);

        var spans = collector.Started
            .Where(a => a.Source.Name == tracedSource.Name && a.DisplayName == "RecordCatalog.ReadPage")
            .ToList();

        spans.Should().HaveCount(4);
        spans.Should().OnlyContain(s => s.ParentSpanId == requestSpanId);

        var spanIds = spans.Select(s => s.SpanId).ToHashSet();
        spans.Should().OnlyContain(s => !spanIds.Contains(s.ParentSpanId));
    }
}