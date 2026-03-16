using OpenTelemetry.Exporter;

namespace Imlinka.SampleWeb.Configuration;

public sealed class OpenTelemetryConfiguration
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>
    /// OpenTelemetry export endpoint URL.
    /// </summary>
    public Uri OtlpEndpoint { get; set; } = new("http://localhost:4317");

    /// <summary>
    /// OpenTelemetry export protocol.
    /// </summary>
    public OtlpExportProtocol OtlpExportProtocol { get; set; } = OtlpExportProtocol.Grpc;
}