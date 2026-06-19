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

1. Install `Imlinka` in the host/app project that produces the final application output, such as a Web API, Worker Service, console app, or Aspire service. Installing it only in a shared `Common` library is not enough to reliably weave the host and all application assemblies.
2. Imlinka weaves the host output and copied local `ProjectReference` assemblies. NuGet dependency DLLs are not rewritten.
3. Signed assemblies are not rewritten because Imlinka does not re-sign assemblies after IL weaving. If a signed project references Imlinka, weaving is skipped with a build warning.
4. You can disable the Imlinka build target in a project with `ImlinkaWeavingEnabled=false`. The switch applies to the project where it is set: if the package is referenced by a host project, it disables weaving for the host output and copied project-reference assemblies handled by that host build. If another project also references Imlinka directly, set the property there as well.

```xml
<PropertyGroup>
  <ImlinkaWeavingEnabled>false</ImlinkaWeavingEnabled>
</PropertyGroup>
```

## Web Sample Project

Check out the [Web Sample Project](../Imlinka.SampleWeb) for a complete example of using Imlinka in an ASP.NET application.

## License

Imlinka is licensed under the MIT License. See the [LICENSE](../LICENSE) file for more details.
