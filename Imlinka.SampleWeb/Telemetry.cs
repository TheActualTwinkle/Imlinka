using System.Diagnostics;

namespace Imlinka.SampleWeb;

/// <summary>
/// Centralized ActivitySource for this service.
/// </summary>
public static class Telemetry
{
    public const string ActivitySourceName = "Imlinka.SampleWeb";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}