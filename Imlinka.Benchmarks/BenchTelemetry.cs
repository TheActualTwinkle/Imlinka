using System.Diagnostics;

namespace Imlinka.Benchmarks;

internal static class BenchTelemetry
{
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    
    private const string ActivitySourceName = "Imlinka.Benchmarks";
}