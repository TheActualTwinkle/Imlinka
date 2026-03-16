# Imlinka

`Imlinka` is a tracing injection library for .NET.
It wraps registered services with `DispatchProxy` and emits `Activity` spans automatically.

## What It Solves

Seeing meaningful method-level spans in traces requires manually wrapping calls in Activity blocks.

Imlinka removes that manual work by allowing you to add method/assembly-level tracing automatically, without handwritten Activity wrappers.

## Installation

```bash
dotnet add package Imlinka
```
## Attribute-Based Tracing

### Use `[Traced]` to trace all public methods of an interface or class.

```csharp
using Imlinka;

[Traced]
public interface IWorker
{
    Task DoWork();
    Task RebuildCache();
}
```

### Use `[Trace]` to trace specific methods of an interface.

```csharp
using Imlinka;

public interface IReportService
{
    [Trace("report.generate")]
    Task<byte[]> GenerateAsync(Guid id);

    [Trace]
    Task UploadAsync(byte[] data);
    
    Task<bool> IsExistsAsync(Guid id);
}
```

If `SpanName` is not provided, the default is `{TypeName}.{MethodName}`.

## DI

Register your services first, then apply tracing injection.

```csharp
using Imlinka;

builder.Services.AddScoped<IWorker, Worker>();
builder.Services.AddScoped<IJumper, Jumper>();
builder.Services.AddScoped<ITester, Tester>();

builder.Services.AddProjectTracingForAssembly(
    typeof(IWorker).Assembly,
    options => options
        //.WithPublicMethodsTracing() // Uncomment to trace all public methods!
        .WithActivitySource(SOME_ACTIVITY_SOURCE) // Sets the ActivitySource to use for emitted spans.
        .IgnoreDefaultNamespaces()); // Ignores 'Microsoft' and 'System' namespaces.
```

_`WithActivitySource(<Source>)` must be same as the one used in your code to configure OpenTelemetry._

If you want to trace all public methods, even those without attributes, use `WithPublicMethodsTracing()`.****

## Web Sample Project

Check out the [Web Sample Project](../Imlinka.SampleWeb) for a complete example of using Imlinka in an ASP.NET application.

## License

Imlinka is licensed under the MIT License. See the [LICENSE](../LICENSE) file for more details.