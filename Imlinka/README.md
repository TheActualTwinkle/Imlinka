# Imlinka

`Imlinka` is a tracing injection library for .NET.
It adds method-level `Activity` spans through build-time IL weaving.

## What It Solves

Seeing meaningful method-level spans in traces requires manually wrapping calls in `Activity` blocks.

Imlinka removes that manual work by allowing you to add method-level and assembly-filtered tracing automatically, without handwritten Activity wrappers.

## Installation

```bash
dotnet add package Imlinka
```
## Attribute-Based Tracing

### Use `[Traced]` to trace all public methods of a class or interface.

```csharp
using Imlinka;

[Traced]
public class Worker
{
    public Task DoWork() => Task.CompletedTask;
    public Task RebuildCache() => Task.CompletedTask;
}
```

### Use `[Trace]` to trace specific methods on a class or interface.

```csharp
using Imlinka;

public class ReportService
{
    [Trace("report.generate")]
    public Task<byte[]> GenerateAsync(Guid id) => Task.FromResult(Array.Empty<byte>());

    [Trace]
    public Task UploadAsync(byte[] data) => Task.CompletedTask;
    
    public Task<bool> ExistsAsync(Guid id) => Task.FromResult(true);
}
```

If `SpanName` ("report.generate") is not provided, the default is `{TypeName}.{MethodName}`.

## DI

Use DI to configure tracing options, such as tracing all public methods or using a custom `ActivitySource`.
IL weaving still happens automatically during build.

```csharp
using Imlinka;

builder.Services.AddProjectTracing(options => options
        .WithActivitySource(SOME_ACTIVITY_SOURCE) // Sets the ActivitySource to use for emitted spans.
        .IgnoreDefaultNamespaces()); // Ignores 'Microsoft' and 'System' namespaces.
```

If you want to trace all public methods, even those without attributes, use `WithPublicMethodsTracing()`.

To restrict tracing to one assembly, use `AddProjectTracingForAssembly(...)`.

```csharp
builder.Services.AddProjectTracingForAssembly(
    typeof(Worker).Assembly,
    options => options
        .WithPublicMethodsTracing()
        .WithActivitySource(SOME_ACTIVITY_SOURCE)
        .IgnoreDefaultNamespaces());
```

## Limitations

For now signed assemblies are not affected because Imlinka does not re-sign assemblies after IL weaving.
If a signed project references Imlinka, weaving is skipped with a build warning.
You can disable weaving for that project with `ImlinkaWeavingEnabled=false`.

```xml
<PropertyGroup>
    <ImlinkaWeavingEnabled>false</ImlinkaWeavingEnabled>
</PropertyGroup>
```

## Web Sample Project

Check out the [Web Sample Project](../Imlinka.SampleWeb) for a complete example of using Imlinka in an ASP.NET application.

## License

Imlinka is licensed under the MIT License. See the [LICENSE](../LICENSE) file for more details.
