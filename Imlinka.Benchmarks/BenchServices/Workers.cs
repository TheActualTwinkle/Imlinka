namespace Imlinka.Benchmarks.BenchServices;

public interface IWorker
{
    Task<int> DoWork();
}

public sealed class WorkerNoTrace(int n) : IWorker
{
    public Task<int> DoWork()
    {
        var result = FibonacciCalculator.Compute(n);
        return Task.FromResult(result);
    }
}

public sealed class WorkerManualTrace(int n) : IWorker
{
    public Task<int> DoWork()
    {
        using var activity = BenchTelemetry.ActivitySource.StartActivity(nameof(WorkerManualTrace));
        var result = FibonacciCalculator.Compute(n);
        return Task.FromResult(result);
    }
}

[Traced]
public interface IWorkerTraced
{
    Task<int> DoWork();
}

public sealed class WorkerTraced(int n) : IWorkerTraced
{
    public Task<int> DoWork()
    {
        var result = FibonacciCalculator.Compute(n);
        return Task.FromResult(result);
    }
}