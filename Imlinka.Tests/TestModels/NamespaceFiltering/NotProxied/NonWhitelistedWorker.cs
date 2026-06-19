namespace Imlinka.Tests.TestModels.NotProxied;

internal interface INonWhitelistedWorker
{
    void Run();
}

internal sealed class NonWhitelistedWorker : INonWhitelistedWorker
{
    public void Run()
    {
    }
}
