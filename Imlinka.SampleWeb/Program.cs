using Imlinka;
using Serilog;
using Imlinka.SampleWeb;
using Imlinka.SampleWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", false, true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
    .AddJsonFile("serilog.json", true, true)
    .AddEnvironmentVariables();

builder.Logging.ClearProviders();

builder.Services.AddOtel(builder);

builder.Host.UseSerilog((context, _, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration), writeToProviders: true);

builder.Services.AddControllers();

builder.Services.AddScoped<IWorker, Worker>();
builder.Services.AddScoped<IJumper, Jumper>();
builder.Services.AddScoped<ITester, Tester>();

builder.Services.AddProjectTracingForAssembly(
    typeof(IWorker).Assembly,
    options => options
        .WithPublicMethodsTracing()
        .WithActivitySource(Telemetry.ActivitySource)
        .IgnoreDefaultNamespaces());

var app = builder.Build();

app.MapControllers();

app.Run();