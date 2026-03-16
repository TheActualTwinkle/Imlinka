using Imlinka.SampleWeb.Configuration;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Imlinka.SampleWeb;

public static class DependencyInjection
{
    public static IServiceCollection AddOtel(this IServiceCollection services, WebApplicationBuilder builder)
    {
        var openTelemetrySection = builder.Configuration.GetSection(OpenTelemetryConfiguration.SectionName);
        services.Configure<OpenTelemetryConfiguration>(openTelemetrySection);

        var configuration = openTelemetrySection.Get<OpenTelemetryConfiguration>()
                            ?? throw new Exception();
        services
            .AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(builder.Environment.ApplicationName))
            .WithLogging(_ => {  }, o => o.IncludeFormattedMessage = true)
            .WithMetrics(b =>
            {
                b.AddAspNetCoreInstrumentation();
                b.AddHttpClientInstrumentation();
                b.AddRuntimeInstrumentation();
            })
            .WithTracing(b =>
            {
                b.AddSource(Telemetry.ActivitySourceName);
                b.AddAspNetCoreInstrumentation();
                b.AddHttpClientInstrumentation();
            })
            .UseOtlpExporter(configuration.OtlpExportProtocol, configuration.OtlpEndpoint);

        return services;
    }
}