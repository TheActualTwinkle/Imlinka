namespace Imlinka.Tests.TestModels.Attributes;

internal interface IValueTaskWorker
{
    ValueTask RunAsync();

    ValueTask<int> CountAsync();
}

internal sealed class ValueTaskWorker : IValueTaskWorker
{
    [Trace]
    public async ValueTask RunAsync()
    {
        await Task.Delay(1);
    }

    [Trace]
    public async ValueTask<int> CountAsync()
    {
        await Task.Delay(1);

        return 42;
    }
}
