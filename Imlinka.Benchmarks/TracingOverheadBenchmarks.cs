using BenchmarkDotNet.Attributes;
using Imlinka.Benchmarks.BenchServices;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace Imlinka.Benchmarks;

[MemoryDiagnoser]
public class WorkerTracingOverheadBenchmarks
{
    private IWorker _worker = null!;
    private IWorkerTraced _workerProxy = null!;
    private IWorker _workerManual = null!;

    // Load parameter: higher N means more CPU work.
    [UsedImplicitly]
    [Params(1, 10_000, 100_000, 1_000_000)]
    public int FibonacciN { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var noTrace = new ServiceCollection();
        noTrace.AddTransient<IWorker>(_ => new WorkerNoTrace(FibonacciN));
        var spNoTrace = noTrace.BuildServiceProvider();
        _worker = spNoTrace.GetRequiredService<IWorker>();

        var manual = new ServiceCollection();
        manual.AddTransient<IWorker>(_ => new WorkerManualTrace(FibonacciN));
        var spManual = manual.BuildServiceProvider();
        _workerManual = spManual.GetRequiredService<IWorker>();
        
        var proxy = new ServiceCollection();
        proxy.AddTransient<IWorkerTraced>(_ => new WorkerTraced(FibonacciN));
        proxy.AddProjectTracingForAssembly(
            typeof(WorkerTraced).Assembly,
            options =>
            {
                options.WithActivitySource(BenchTelemetry.ActivitySource);
                options.TraceAllPublicMethods = true;
            });
        var spProxy = proxy.BuildServiceProvider();
        _workerProxy = spProxy.GetRequiredService<IWorkerTraced>();
    }

    [Benchmark(Baseline = true)]
    public async Task<int> CountFibonacci_NoTrace() => await _worker.DoWork();

    [Benchmark]
    public async Task<int> CountFibonacci_ManualTrace() => await _workerManual.DoWork();

    [Benchmark]
    public async Task<int> Fibonacci_Traced() => await _workerProxy.DoWork();
}