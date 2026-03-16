namespace Imlinka.SampleWeb.Services;

public sealed class Worker : IWorker
{
    public async Task DoWork()
    {
        await Task.Delay(1000);
    }
}